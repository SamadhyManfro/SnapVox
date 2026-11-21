using System.Diagnostics;
using Avalonia.Threading;

namespace SnapVox;

/// <summary>
/// Safety net for the full-screen dimming, which sits on top of everything: if the UI stops responding
/// while it is open, SnapVox restarts itself so the user never gets locked out of Windows.
/// </summary>
internal static class UiWatchdog
{
    private const int CheckMs = 500;
    private const int FrozenMs = 6000;
    private const int ClockJumpMs = 2500; // the PC slept: not a freeze

    private static readonly object Gate = new();
    private static Thread? _thread;
    private static long _lastBeat;
    private static volatile bool _watching;
    private static volatile string _step = "";

    /// <summary>What the session is doing now; written to the log if it freezes.</summary>
    public static void Step(string step) => _step = step;

    public static void Start()
    {
        Volatile.Write(ref _lastBeat, Environment.TickCount64);
        _watching = true;
        lock (Gate)
        {
            _thread ??= StartThread();
        }
    }

    public static void Stop() => _watching = false;

    private static Thread StartThread()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "SnapVox.Watchdog" };
        thread.Start();
        return thread;
    }

    private static void Run()
    {
        long previous = Environment.TickCount64;
        while (true)
        {
            Thread.Sleep(CheckMs);
            long now = Environment.TickCount64;
            bool clockJumped = now - previous > ClockJumpMs;
            previous = now;

            if (!_watching)
                continue;
            if (clockJumped)
            {
                Volatile.Write(ref _lastBeat, now);
                continue;
            }

            Dispatcher.UIThread.Post(() => Volatile.Write(ref _lastBeat, Environment.TickCount64));
            if (now - Volatile.Read(ref _lastBeat) > FrozenMs)
                Recover();
        }
    }

    private static void Recover()
    {
        Log.Info($"UI frozen for over {FrozenMs / 1000} s during \"{_step}\"; restarting SnapVox.");
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                Process.Start(new ProcessStartInfo(exe)
                {
                    ArgumentList = { AppRestart.WaitArg, Environment.ProcessId.ToString() },
                    UseShellExecute = false,
                });
            }
        }
        catch (Exception ex)
        {
            Log.Info("Couldn't start a new copy: " + ex.Message);
        }
        Process.GetCurrentProcess().Kill(); // removes the dimming even with the UI thread stuck
    }
}
