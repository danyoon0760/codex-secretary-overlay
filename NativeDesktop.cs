using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SecretaryOverlay;

internal static partial class NativeDesktop
{
    public delegate IntPtr MouseProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }

    public static bool IsInteractiveDesktop()
    {
        var desktop = OpenInputDesktop(0, false, 0x100);
        if (desktop == IntPtr.Zero) return false;
        try { return SwitchDesktop(desktop); }
        finally { CloseDesktop(desktop); }
    }

    public static string ForegroundProcessName()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch { return ""; }
    }

    public static Rectangle ActiveScreenBounds() => System.Windows.Forms.Screen.FromHandle(GetForegroundWindow()).Bounds;

    public static void Capture(string path, Rectangle bounds, params IntPtr[] excludedWindows)
    {
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        var restore = new List<(IntPtr Window, uint Affinity)>();
        try
        {
            // Exclude the overlay only during this frame, never while waiting for the model
            // or between video frames. Keep this synchronous so no UI work interleaves.
            foreach (var window in excludedWindows.Where(h => h != IntPtr.Zero).Distinct())
            {
                if (!GetWindowDisplayAffinity(window, out uint previous))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if (!SetWindowDisplayAffinity(window, 0x11))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                restore.Add((window, previous));
            }
            if (restore.Count > 0) Marshal.ThrowExceptionForHR(DwmFlush());
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }
        finally
        {
            foreach (var (window, affinity) in restore)
                if (!SetWindowDisplayAffinity(window, affinity))
                    AppStorage.LogError(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "캡처 후 창 표시 설정을 복원하지 못했습니다."));
            if (restore.Count > 0) DwmFlush();
        }
        double scale = Math.Min(1, 1600.0 / Math.Max(bounds.Width, bounds.Height));
        using var reduced = new Bitmap(bitmap, new Size(Math.Max(1, (int)(bounds.Width * scale)), Math.Max(1, (int)(bounds.Height * scale))));
        reduced.Save(path, ImageFormat.Png);
    }

    public static void MakePassive(IntPtr window)
    {
        var style = GetWindowLongPtr(window, -20).ToInt64();
        SetWindowLongPtr(window, -20, new IntPtr(style | 0x08000000 | 0x80));
    }

    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool SwitchDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, MouseProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowDisplayAffinity(IntPtr h, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr h, uint affinity);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] public static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid setting, uint flags);
    [DllImport("user32.dll")] public static extern bool UnregisterPowerSettingNotification(IntPtr h);
}
