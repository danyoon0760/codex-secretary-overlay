using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace SecretaryOverlay;

internal sealed record ObservedWindow(IntPtr Handle, uint ProcessId, string AppName, string Title);

internal static partial class NativeDesktop
{
    private const int ExtendedFrameBounds = 9, Cloaked = 14;
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    private delegate bool WindowCallback(IntPtr window, IntPtr data);

    public static ObservedWindow GetCommentaryWindow()
    {
        var window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out uint owner);
        if (owner == Environment.ProcessId)
        {
            // A pet settings/menu window may briefly own focus. Skip every window of this
            // process and choose the first usable app in the desktop stacking order.
            window = IntPtr.Zero;
            EnumWindows((candidate, _) =>
            {
                if (!IsCommentaryWindow(candidate)) return true;
                window = candidate;
                return false;
            }, IntPtr.Zero);
        }
        if (!IsCommentaryWindow(window))
            throw new InvalidOperationException("지금 보고 있는 앱 창을 찾지 못했어. 앱을 앞에 띄운 뒤 다시 불러줘.");
        GetWindowThreadProcessId(window, out owner);
        string appName = "";
        try { using var process = Process.GetProcessById((int)owner); appName = process.ProcessName; }
        catch (ArgumentException) { }
        var title = new StringBuilder(257);
        GetWindowText(window, title, title.Capacity);
        return new(window, owner, appName, title.ToString());
    }

    internal static bool IsCommentaryWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window)) return false;
        GetWindowThreadProcessId(window, out uint owner);
        if (owner == 0 || owner == Environment.ProcessId) return false;
        if (DwmGetWindowAttribute(window, Cloaked, out uint cloaked, sizeof(uint)) != 0 || cloaked != 0) return false;
        var className = new StringBuilder(128);
        GetClassName(window, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768") return false;
        // Tool windows are typically floating palettes/overlays, not the app being used.
        return (GetWindowLongPtr(window, -20).ToInt64() & 0x80) == 0;
    }

    public static void CaptureWindow(string path, ObservedWindow observed, params IntPtr[] excludedWindows)
    {
        // A second video frame must never silently capture an app the user switched to.
        var current = GetCommentaryWindow();
        if (current.Handle != observed.Handle || current.ProcessId != observed.ProcessId)
            throw new InvalidOperationException("창이 바뀌어서 이번 한마디는 건너뛰었어.");
        var previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4)); // Per-monitor v2: physical screen pixels.
        try
        {
            var bounds = GetCaptureBounds(observed.Handle);
            Capture(path, bounds, excludedWindows);
        }
        finally
        {
            if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi);
        }
    }

    internal static Rectangle GetCaptureBounds(IntPtr window)
    {
        if (!IsCommentaryWindow(window) || DwmGetWindowAttribute(window, ExtendedFrameBounds, out NativeRect bounds, Marshal.SizeOf<NativeRect>()) != 0)
            throw new InvalidOperationException("현재 창의 화면을 확인하지 못했어.");
        var clipped = Rectangle.Intersect(Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            System.Windows.Forms.SystemInformation.VirtualScreen);
        if (clipped.Width <= 0 || clipped.Height <= 0)
            throw new InvalidOperationException("현재 창이 화면 밖에 있어.");
        return clipped;
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out uint value, int size);
}
