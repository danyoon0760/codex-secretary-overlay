using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace SecretaryOverlay;

internal static class CaptureVerification
{
    public static int CheckForeground()
    {
        var path = Path.Combine(Path.GetTempPath(), "secretary-window-test-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var window = NativeDesktop.GetCommentaryWindow();
            bool rejected = false;
            try { NativeDesktop.CaptureWindow(path, window with { ProcessId = window.ProcessId + 1 }); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || File.Exists(path)) throw new InvalidOperationException("A changed capture target was not rejected before capture.");
            NativeDesktop.CaptureWindow(path, window);
            using var image = System.Drawing.Image.FromFile(path);
            // Store dimensions only; never persist screen contents or window titles.
            Directory.CreateDirectory(AppStorage.DataDirectory);
            File.WriteAllText(Path.Combine(AppStorage.DataDirectory, "capture-window-check.json"),
                System.Text.Json.JsonSerializer.Serialize(new { ok = image.Width > 0 && image.Height > 0, changedTargetRejected = rejected, width = image.Width, height = image.Height }, AppStorage.Json));
            return 0;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    public static void Check(Action<bool, string> check)
    {
        // Real layered HWNDs exercise Windows display-affinity behavior without showing UI.
        var owner = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true };
        var bubble = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true };
        var path = Path.Combine(Path.GetTempPath(), "secretary-capture-test-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var ownerHandle = new WindowInteropHelper(owner).EnsureHandle();
            var bubbleHandle = new WindowInteropHelper(bubble).EnsureHandle();
            NativeDesktop.MakePassive(ownerHandle);
            NativeDesktop.MakePassive(bubbleHandle);
            check(!NativeDesktop.IsCommentaryWindow(ownerHandle) && !NativeDesktop.IsCommentaryWindow(bubbleHandle) && !NativeDesktop.IsCommentaryWindow(IntPtr.Zero),
                "Pet, bubble and absent windows cannot become commentary capture targets");
            bool Capturable(IntPtr handle) => NativeDesktop.GetWindowDisplayAffinity(handle, out uint affinity) && affinity == 0;
            check(Capturable(ownerHandle) && Capturable(bubbleHandle), "Pet and speech bubble allow ordinary screenshots by default");
            var screen = NativeDesktop.ActiveScreenBounds();
            var pixel = new Rectangle(screen.Left, screen.Top, 1, 1);
            NativeDesktop.Capture(path, pixel, ownerHandle, bubbleHandle);
            check(File.Exists(path) && Capturable(ownerHandle) && Capturable(bubbleHandle), "Own capture restores both windows immediately");

            bool failed = false;
            try { NativeDesktop.Capture(path, pixel, ownerHandle, bubbleHandle, new IntPtr(-1)); }
            catch (Win32Exception) { failed = true; }
            check(failed && Capturable(ownerHandle) && Capturable(bubbleHandle), "Partial capture setup failure restores all changed windows");
        }
        finally
        {
            bubble.Close();
            owner.Close();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
