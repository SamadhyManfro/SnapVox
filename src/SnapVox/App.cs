using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using SnapVox.Localization;
using SnapVox.Platform;
using SnapVox.UI;

namespace SnapVox;

public partial class App : Application
{
    private IHotkeyService? _hotkey;
    private TrayIcon? _tray;
    private HotkeyGesture _activeHotkey = HotkeyGesture.Default;

    // No .axaml files: everything in code. Lighter, AOT friendly, and it avoids the
    // XAML compilation step (which Smart App Control blocked now and then on the dev PC).
    public override void Initialize()
    {
        Name = "SnapVox";
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _hotkey = PlatformServices.CreateHotkeyService();
            _hotkey.Pressed += () => Dispatcher.UIThread.Post(OnActivate);
            bool registered = _hotkey.Register(_activeHotkey);
            Log.Info($"Started. Hotkey {_activeHotkey} registered: {registered}");

            SetupTray(desktop);
            Updater.CheckOnStartup(); // if "Automatic updates" is on, asks when there is a new version

            if (registered)
                IslandToast.Show(L.Ready(_activeHotkey.ToString()));
            else
                IslandToast.Show(L.HotkeyInUse(_activeHotkey.ToString()), isWarning: true);

            desktop.Exit += (_, _) =>
            {
                _hotkey?.Dispose();
                _tray?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _tray = new TrayIcon
        {
            Icon = LogoRenderer.CreateWindowIcon(32),
            ToolTipText = "SnapVox",
            Menu = BuildTrayMenu(desktop),
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => OnActivate();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    private NativeMenu BuildTrayMenu(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var read = new NativeMenuItem($"{L.ReadArea}   ({_activeHotkey})");
        read.Click += (_, _) => OnActivate();

        var settings = new NativeMenuItem(L.SettingsMenu);
        settings.Click += (_, _) => SettingsWindow.Open();

        var suggestion = new NativeMenuItem(L.SendSuggestion);
        suggestion.Click += (_, _) => Updater.OpenSuggestion();

        var restart = new NativeMenuItem(L.RestartApp);
        restart.Click += (_, _) => AppRestart.Restart();

        var quit = new NativeMenuItem(L.Quit);
        quit.Click += (_, _) => Quit(desktop);

        var menu = new NativeMenu();
        menu.Add(read);
        menu.Add(settings);
        menu.Add(suggestion);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(restart);
        menu.Add(quit);
        return menu;
    }

    private readonly SelectionSession _session = new();

    /// <summary>Quit: stops the reading, closes every open window and menu, and ends the process.</summary>
    private void Quit(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _session.Close();
        foreach (var w in desktop.Windows.ToList())
            w.Close();
        desktop.Shutdown();
    }

    /// <summary>Called by the global hotkey or the tray: opens the selection on every monitor.</summary>
    private void OnActivate()
    {
        Log.Info("Activated (hotkey or tray).");
        try
        {
            _session.Start();
        }
        catch (Exception ex)
        {
            Log.Info("Error starting the selection: " + ex);
            _session.Close();
        }
    }
}
