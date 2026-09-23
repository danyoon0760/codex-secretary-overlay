using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Point = System.Windows.Point;

namespace SecretaryOverlay;

public sealed partial class PetWindow
{
    private const int ExtendedStyleIndex = -20;
    private const long NoActivateStyle = 0x08000000;
    private const long ToolWindowStyle = 0x80;
    private const long TransparentStyle = 0x20;
    private const int MouseActivateMessage = 0x21;
    private const int HitTestMessage = 0x84;
    private const int NoActivateResult = 3;
    private const int TransparentHitResult = -1;
    private IntPtr hwnd;
    private IntPtr powerNotification;

    private void InitializeNativeWindow()
    {
        hwnd = new WindowInteropHelper(this).Handle;
        NativeDesktop.MakePassive(hwnd);
        var display = new Guid("6fe69556-704a-47a0-8f24-c28d936fda47");
        powerNotification = NativeDesktop.RegisterPowerSettingNotification(hwnd, ref display, 0);
        var style = GetWindowLongPtr(hwnd, ExtendedStyleIndex).ToInt64();
        SetWindowLongPtr(hwnd, ExtendedStyleIndex, new IntPtr(style | NoActivateStyle | ToolWindowStyle));
        HwndSource.FromHwnd(hwnd)?.AddHook(WindowHook);
    }

    private void ApplyClickThrough()
    {
        var style = GetWindowLongPtr(hwnd, ExtendedStyleIndex).ToInt64();
        SetWindowLongPtr(hwnd, ExtendedStyleIndex,
            new IntPtr(clickThrough ? style | TransparentStyle : style & ~TransparentStyle));
    }

    private IntPtr WindowHook(IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled)
    {
        if (message == 0x218 && w.ToInt64() == 0x8013 && l != IntPtr.Zero && activity is not null)
        {
            var setting = Marshal.PtrToStructure<Guid>(l);
            if (setting == new Guid("6fe69556-704a-47a0-8f24-c28d936fda47"))
                activity.DisplayOn = Marshal.ReadInt32(l, 20) != 0;
        }
        if (message == MouseActivateMessage)
        {
            handled = true;
            return new IntPtr(NoActivateResult);
        }
        if (message == HitTestMessage && !clickThrough)
        {
            long value = l.ToInt64();
            var point = PointFromScreen(new Point((short)(value & 0xffff), (short)((value >> 16) & 0xffff)));
            if (point.Y >= ActualHeight - BadgeHeight && showLabels) return IntPtr.Zero;
            if (HitTestCharacter(point)) return IntPtr.Zero;
            handled = true;
            return new IntPtr(TransparentHitResult);
        }
        return IntPtr.Zero;
    }

    private bool HitTestCharacter(Point point)
    {
        if (front is null) return false;
        var image = assets[activeFile];
        // Follow the displayed pose's placement, breathing and nodding transforms.
        var inverse = front.TransformToAncestor(this).Inverse;
        if (inverse is null || front.ActualWidth <= 0 || front.ActualHeight <= 0) return false;
        var local = inverse.Transform(point);
        int x = (int)Math.Floor(local.X * image.PixelWidth / front.ActualWidth);
        int y = (int)Math.Floor(local.Y * image.PixelHeight / front.ActualHeight);
        return assets.IsOpaque(activeFile, x, y);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr h, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr h, int index, IntPtr value);
}
