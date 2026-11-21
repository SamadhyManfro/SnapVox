using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace SnapVox;

/// <summary>
/// Restarts SnapVox: starts a new copy that waits for this one to exit (because of the single-instance rule)
/// and then closes this one.
/// </summary>
internal static class AppRestart
{
    public const string WaitArg = "--wait-for-exit";

    public static void Restart()
    {
        if (Environment.ProcessPath is not { } exe)
            return;

        Log.Info("Reiniciando o SnapVox.");
        Process.Start(new ProcessStartInfo(exe)
        {
            ArgumentList = { WaitArg, Environment.ProcessId.ToString() },
            UseShellExecute = false,
        });

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var w in desktop.Windows.ToList())
                w.Close(); // also closes open menus/windows
            desktop.Shutdown();
        }
    }

    /// <summary>In the new copy: waits for the previous one to exit before continuing (max 10 s).</summary>
    public static void WaitIfRestarting(string[] args)
    {
        int i = Array.IndexOf(args, WaitArg);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out int pid))
            return;
        try
        {
            using var old = Process.GetProcessById(pid);
            old.WaitForExit(10_000);
        }
        catch (ArgumentException)
        {
            // already exited
        }
    }
}
