using Avalonia;
using Avalonia.Controls;
using SnapVox.Platform.Windows;

namespace SnapVox.Platform;

/// <summary>Picks each service's implementation for the current operating system.</summary>
internal static class PlatformServices
{
    public static IHotkeyService CreateHotkeyService()
    {
        if (OperatingSystem.IsWindows())
            return new WinHotkeyService();

        // Linux and Mac come later (SharpHook).
        throw new PlatformNotSupportedException("SnapVox supports only Windows for now.");
    }

    public static IScreenCapture CreateScreenCapture()
    {
        if (OperatingSystem.IsWindows())
            return new WinScreenCapture();

        throw new PlatformNotSupportedException("SnapVox supports only Windows for now.");
    }

    public static IOcrEngine CreateOcrEngine()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return new WinOcrEngine();

        throw new PlatformNotSupportedException("OCR is Windows-only for now.");
    }

    public static Speech.ITtsEngine CreateOfflineTts()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return new WinTts();

        throw new PlatformNotSupportedException("The offline voice is Windows-only for now.");
    }

    public static IAudioPlayer CreateAudioPlayer()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return new WinAudioPlayer();

        throw new PlatformNotSupportedException("Audio is Windows-only for now.");
    }

    public static IAudioExporter CreateAudioExporter()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return new WinAudioExporter();

        throw new PlatformNotSupportedException("Saving MP3 is Windows-only for now.");
    }

    public static IOcrLanguageInstaller CreateOcrLanguageInstaller()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            return new WinOcrLanguageInstaller();

        throw new PlatformNotSupportedException("Language installation is Windows-only for now.");
    }

    public static IStartupService CreateStartupService()
    {
        if (OperatingSystem.IsWindows())
            return new WinStartupService();

        throw new PlatformNotSupportedException("Start with the system is Windows-only for now.");
    }

    public static ILanguageDetector CreateLanguageDetector()
    {
        if (OperatingSystem.IsWindows())
            return new WinLanguageDetector();

        throw new PlatformNotSupportedException("Language detection is Windows-only for now.");
    }

    /// <summary>Current mouse position in physical screen pixels.</summary>
    public static PixelPoint GetCursorPosition()
    {
        if (OperatingSystem.IsWindows())
            return WinCursor.GetPosition();

        return new PixelPoint(0, 0);
    }

    /// <summary>Lets clicks and scrolling go through the window.</summary>
    public static void SetClickThrough(Window window, bool enabled)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is { } hwnd)
            WinWindowStyle.SetClickThrough(hwnd, enabled);
    }

    /// <summary>Keeps the window out of screen captures (our own windows never end up in the OCR image).</summary>
    public static void ExcludeFromCapture(Window window)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is { } hwnd)
            WinWindowStyle.ExcludeFromCapture(hwnd);
    }

    /// <summary>Sends a mouse-wheel turn to the app under the cursor.</summary>
    public static void SendWheel(int delta, bool horizontal)
    {
        if (OperatingSystem.IsWindows())
            WinWindowStyle.SendWheel(delta, horizontal);
    }

    /// <summary>Brings the window above every other "always on top" window, without taking focus.</summary>
    public static void BringToTop(Window? window)
    {
        if (window is not null && OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is { } hwnd)
            WinWindowStyle.BringToTop(hwnd);
    }

    /// <summary>Hides the window from Alt+Tab; optionally keeps it from taking focus.</summary>
    public static void MakeToolWindow(Window window, bool noActivate)
    {
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle()?.Handle is { } hwnd)
            WinWindowStyle.SetToolWindow(hwnd, noActivate);
    }
}
