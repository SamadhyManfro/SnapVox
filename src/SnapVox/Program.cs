using Avalonia;

namespace SnapVox;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppPaths.PreparePortableRuntime();

        // Developer tool: draws the overlay into PNGs and exits (opens no windows).
        if (args.Length == 2 && args[0] == "--render-test")
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            return DevTools.RenderTest.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--capture-test")
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            return DevTools.CaptureTest.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--make-ico")
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            return DevTools.IconMaker.Run(args[1]);
        }
        if (args.Length == 2 && args[0] == "--render-island")
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            UI.IslandWindow.RenderPreview(ScreenInfo.All[0], args[1]);
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--render-settings")
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            UI.SettingsWindow.RenderPreview(args[1], args.Length > 2 ? args[2] : null);
            return 0;
        }
        if (args.Length == 3 && args[0] == "--edge-token")
        {
            // Compare with the original edge-tts: writes the token generated for a fixed time.
            File.WriteAllText(args[2], EdgeTts.EdgeDrm.GenerateSecMsGec(double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture)));
            return 0;
        }
        if (args.Length == 3 && args[0] == "--detect")
        {
            // Diagnostics: language candidates from the Windows detector for a text.
            var detector = Platform.PlatformServices.CreateLanguageDetector();
            var recognizer = new Ocr.TextRecognizer(Platform.PlatformServices.CreateOcrEngine(), detector);
            File.WriteAllText(args[2], string.Join(",", detector.DetectAll(args[1])) + " | chosen: " + recognizer.DetectForTest(args[1], "en"));
            return 0;
        }
        if (args.Length == 2 && args[0] == "--mp3-test")
        {
            // Generates speech (Windows voice, WAV) and converts it to MP3 with the Windows encoder.
            Directory.CreateDirectory(args[1]);
            var audio = Platform.PlatformServices.CreateOfflineTts().SynthesizeAsync("SnapVox MP3 test.", "en", null, CancellationToken.None).GetAwaiter().GetResult();
            var mp3 = Platform.PlatformServices.CreateAudioExporter().ToMp3Async(audio.Audio, audio.MimeType).GetAwaiter().GetResult();
            File.WriteAllBytes(Path.Combine(args[1], "test.mp3"), mp3);
            File.WriteAllText(Path.Combine(args[1], "mp3-report.txt"),
                $"wav {audio.Audio.Length} bytes → mp3 {mp3.Length} bytes; Downloads = {Platform.PlatformServices.CreateAudioExporter().DownloadsFolder}");
            return 0;
        }
        if (args.Length == 2 && args[0] == "--tts-test")
            return DevTools.TtsTest.Run(args[1]);
        if (args.Length == 2 && args[0] == "--ocr-test")
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            return DevTools.OcrTest.Run(args[1]);
        }

        // Came from "Restart": wait for the previous copy to exit and release the single instance.
        AppRestart.WaitIfRestarting(args);

        // Only one SnapVox instance per user.
        using var single = new Mutex(true, @"Local\SnapVox.SingleInstance", out bool isFirst);
        if (!isFirst)
            return 0;

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
