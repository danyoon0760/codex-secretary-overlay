using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Windows.Media;
using Windows.Media.Control;

namespace SecretaryOverlay;

internal sealed record ViewingState(bool VideoPlaying, string Title = "");

internal sealed class DesktopActivity : IDisposable
{
    private readonly NativeDesktop.MouseProc callback;
    private readonly IntPtr hook;
    private long lastMouse = long.MinValue;
    private bool suspended;
    private bool locked;
    public bool DisplayOn { get; set; } = true;
    private GlobalSystemMediaTransportControlsSessionManager? media;
    private long retryMediaAt;

    public DesktopActivity()
    {
        callback = OnMouse;
        hook = NativeDesktop.SetWindowsHookEx(14, callback, NativeDesktop.GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) AppStorage.LogError(new InvalidOperationException("마우스 활동 감지를 시작하지 못했습니다."));
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public bool Available => !suspended && !locked && DisplayOn && NativeDesktop.IsInteractiveDesktop();
    public bool MouseRecent(long now) => lastMouse != long.MinValue && now - lastMouse <= CommentaryPolicy.MouseRecentMs;

    private IntPtr OnMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var input = Marshal.PtrToStructure<NativeDesktop.MouseInput>(data);
            if ((input.Flags & 3) == 0) lastMouse = Environment.TickCount64;
        }
        return NativeDesktop.CallNextHookEx(hook, code, message, data);
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.RemoteDisconnect)
            locked = true;
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.RemoteConnect)
        { locked = false; lastMouse = long.MinValue; }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        suspended = e.Mode == PowerModes.Suspend || (suspended && e.Mode != PowerModes.Resume);
        if (e.Mode == PowerModes.Resume) lastMouse = long.MinValue;
    }

    public async Task<ViewingState> GetViewingStateAsync(CancellationToken token)
    {
        if (!Available) return new(false);
        try
        {
            if (media is null)
            {
                if (Environment.TickCount64 < retryMediaAt) return new(false);
                media = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(3), token);
            }
            var foreground = NativeDesktop.ForegroundProcessName();
            foreach (var session in media.GetSessions())
            {
                if (session.GetPlaybackInfo().PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing) continue;
                var info = session.GetPlaybackInfo();
                if (info.PlaybackType == MediaPlaybackType.Music || info.PlaybackType == MediaPlaybackType.Image) continue;
                // Background music must not count as watching a video. Only a foreground media app qualifies.
                if (!MatchesForeground(session.SourceAppUserModelId, foreground)) continue;
                var properties = await session.TryGetMediaPropertiesAsync().AsTask(token).WaitAsync(TimeSpan.FromSeconds(2), token);
                return new(true, properties.Title ?? "");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            media = null;
            retryMediaAt = Environment.TickCount64 + 60_000;
        }
        return new(false);
    }

    internal static bool MatchesForeground(string appId, string foreground) =>
        foreground.Length > 0 && (appId.Contains(foreground, StringComparison.OrdinalIgnoreCase)
        || (foreground.Equals("msedge", StringComparison.OrdinalIgnoreCase) && appId.Contains("MicrosoftEdge", StringComparison.OrdinalIgnoreCase)));

    public void Dispose()
    {
        if (hook != IntPtr.Zero) NativeDesktop.UnhookWindowsHookEx(hook);
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        GC.KeepAlive(callback);
    }
}
