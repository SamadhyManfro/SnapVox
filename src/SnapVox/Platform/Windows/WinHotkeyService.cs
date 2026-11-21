using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SnapVox.Platform.Windows;

/// <summary>
/// Global hotkey via Windows RegisterHotKey. No keyboard hook:
/// Windows itself tells us when the combination is pressed, so it costs nothing while idle.
/// Runs on its own thread with a message queue.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WinHotkeyService : IHotkeyService
{
    private const int HotkeyId = 0x5356; // "SV"
    private const uint WM_HOTKEY = 0x0312;
    private const uint WM_QUIT = 0x0012;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    private Thread? _thread;
    private uint _threadId;

    public event Action? Pressed;

    public bool Register(HotkeyGesture gesture)
    {
        Unregister();

        uint mods = MOD_NOREPEAT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= MOD_ALT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) mods |= MOD_CONTROL;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= MOD_SHIFT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Win)) mods |= MOD_WIN;
        uint vk = char.ToUpperInvariant(gesture.Key); // VK of A–Z and 0–9 = ASCII code (Esc = 0x1B)

        bool ok = false;
        using var ready = new ManualResetEventSlim();

        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            PeekMessageW(out _, IntPtr.Zero, 0, 0, 0); // makes sure the message queue exists

            ok = RegisterHotKey(IntPtr.Zero, HotkeyId, mods, vk);
            ready.Set();
            if (!ok)
                return;

            while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                if (msg.message == WM_HOTKEY && (int)msg.wParam == HotkeyId)
                    Pressed?.Invoke();
            }

            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        })
        {
            IsBackground = true,
            Name = "SnapVox.Hotkey",
        };

        _thread.Start();
        ready.Wait();

        if (!ok)
            _thread = null;

        return ok;
    }

    private void Unregister()
    {
        if (_thread is null)
            return;

        PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        _thread.Join(500);
        _thread = null;
    }

    public void Dispose() => Unregister();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
        public uint lPrivate;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint idThread, uint msg, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
