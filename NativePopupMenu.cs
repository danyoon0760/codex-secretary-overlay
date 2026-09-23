using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SecretaryOverlay;

// HMENU is rendered and operated by Windows; no owner drawing or custom fonts.
internal sealed class NativePopupMenu : IDisposable
{
    private IntPtr handle = CreatePopupMenu();
    private readonly Dictionary<uint, Action> commands = new();
    private const uint Checked = 0x8, Grayed = 0x1, Separator = 0x800;
    private const uint ReturnCommand = 0x100, NoNotify = 0x80, RightButton = 0x2, RightAlign = 0x8;

    public NativePopupMenu()
    {
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Add(string label, Action action, bool isChecked = false, bool enabled = true)
    {
        uint id = (uint)commands.Count + 1;
        uint flags = (isChecked ? Checked : 0) | (enabled ? 0 : Grayed);
        if (!AppendMenu(handle, flags, (UIntPtr)id, label))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        commands.Add(id, action);
    }

    public void AddSeparator()
    {
        if (!AppendMenu(handle, Separator, UIntPtr.Zero, null))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void AddEntries(IEnumerable<MenuEntry> entries) => AddEntries(handle, entries);

    private void AddEntries(IntPtr parent, IEnumerable<MenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            uint flags = (entry.Checked ? Checked : 0) | (entry.Enabled ? 0 : Grayed);
            if (entry.Children is not null)
            {
                var child = CreatePopupMenu();
                if (child == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    AddEntries(child, entry.Children);
                    if (!AppendMenu(parent, flags | 0x10 /* MF_POPUP */, (UIntPtr)child, entry.Label))
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                catch { DestroyMenu(child); throw; }
                // Parent now owns the child handle, recursively destroyed with the root menu.
            }
            else if (entry.Label.Length == 0)
            {
                if (!AppendMenu(parent, Separator, UIntPtr.Zero, null)) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            else
            {
                uint id = (uint)commands.Count + 1;
                if (!AppendMenu(parent, flags, (UIntPtr)id, entry.Label)) throw new Win32Exception(Marshal.GetLastWin32Error());
                commands.Add(id, entry.Action ?? (() => { }));
            }
        }
    }

    internal IntPtr Handle => handle;
    internal Action? Resolve(uint command) => commands.GetValueOrDefault(command);

    public Action? Select(IntPtr owner)
    {
        if (!GetCursorPos(out var cursor)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var previous = GetForegroundWindow();
        uint selected;
        // The foreground owner lets Windows dismiss the menu on an outside click.
        SetForegroundWindow(owner);
        try
        {
            uint alignment = GetSystemMetrics(40 /* SM_MENUDROPALIGNMENT */) != 0 ? RightAlign : 0;
            selected = TrackPopupMenuEx(handle, ReturnCommand | NoNotify | RightButton | alignment,
                cursor.X, cursor.Y, owner, IntPtr.Zero);
        }
        finally
        {
            PostMessage(owner, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
            // Keep manual screen commentary aimed at the app the user was using.
            // An outside click may already have activated a different app: leave it alone.
            if (previous != IntPtr.Zero && previous != owner && GetForegroundWindow() == owner)
                SetForegroundWindow(previous);
        }
        return Resolve(selected);
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        DestroyMenu(handle);
        handle = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr owner, IntPtr parameters);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
