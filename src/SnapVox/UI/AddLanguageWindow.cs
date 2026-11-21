using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using SnapVox.Localization;
using SnapVox.Platform;
using SnapVox.Speech;

namespace SnapVox.UI;

/// <summary>Searchable list of the languages that can still be added. Answers a 2-letter code, or null if closed.</summary>
internal sealed class AddLanguageWindow : Window
{
    private static readonly IBrush Muted = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
    private static readonly IBrush RowHover = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
    private static readonly IBrush Field = new SolidColorBrush(Color.Parse("#1C1C20"));

    private readonly TaskCompletionSource<string?> _answer = new();
    private readonly TextBox _search;
    private readonly StackPanel _list = new() { Spacing = 1 };
    private readonly IReadOnlyList<(string Code, string Name, string Native, string Keys)> _languages;

    public static AddLanguageWindow? Current { get; private set; }

    public static Task<string?> AskAsync()
    {
        if (Current is { } open)
        {
            open.Activate();
            return open._answer.Task;
        }

        var window = new AddLanguageWindow();
        Current = window;
        window.Closed += (_, _) =>
        {
            Current = null;
            window._answer.TrySetResult(null);
        };
        window.Show();
        return window._answer.Task;
    }

    public static void CloseCurrent() => Current?.Close();

    private AddLanguageWindow()
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = L.AddLanguageTitle;
        Cursor = new Cursor(StandardCursorType.Arrow);

        _languages = VoiceCatalog.LanguagesToAdd.Select(Describe).OrderBy(l => l.Name, StringComparer.CurrentCulture).ToList();

        _search = new TextBox
        {
            PlaceholderText = L.AddLanguageSearch,
            FontSize = 13,
            Foreground = Brushes.White,
            Background = Field,
            BorderThickness = new Thickness(0.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 7),
        };
        _search.TextChanged += (_, _) => Fill();
        _search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Matches().FirstOrDefault() is { Code: not null } first)
            {
                Answer(first.Code);
                e.Handled = true;
            }
        };

        Content = new Border
        {
            Width = 360,
            Background = new SolidColorBrush(Color.Parse("#0B0B0D")),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(0.5),
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(14),
            Margin = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 9,
                Children =
                {
                    new TextBlock { Text = L.AddLanguageTitle, FontSize = 14, FontWeight = FontWeight.Medium, Foreground = Brushes.White, Margin = new Thickness(4, 0) },
                    _search,
                    new ScrollViewer { Height = 260, Content = _list },
                    new TextBlock { Text = L.AddLanguageHint, FontSize = 11, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0) },
                },
            },
        };
        Fill();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        Deactivated += (_, _) => Close();
        Opened += (_, _) =>
        {
            PlatformServices.MakeToolWindow(this, noActivate: false);
            PlatformServices.ExcludeFromCapture(this);
            var cursor = PlatformServices.GetCursorPosition();
            if ((Screens.ScreenFromPoint(cursor) ?? Screens.Primary) is { } screen)
            {
                var area = screen.WorkingArea;
                int width = (int)(Bounds.Width * RenderScaling), height = (int)(Bounds.Height * RenderScaling);
                Position = new PixelPoint(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2);
            }
            PlatformServices.BringToTop(this);
            Activate();
            _search.Focus();
        };
    }

    private static (string Code, string Name, string Native, string Keys) Describe(string code)
    {
        string name = L.LanguageName(code), english = code, native = code;
        try
        {
            var culture = new CultureInfo(code);
            english = culture.EnglishName.Split(" (")[0];
            native = culture.NativeName.Split(" (")[0];
        }
        catch (CultureNotFoundException)
        {
            // keeps the code as the name
        }
        return (code, name, native, Normalize($"{name} {english} {L.PortugueseName(code)} {native} {code}"));
    }

    private IEnumerable<(string Code, string Name, string Native, string Keys)> Matches()
    {
        string query = Normalize(_search.Text ?? "");
        return _languages
            .Where(l => l.Keys.Contains(query, StringComparison.Ordinal))
            .OrderBy(l => Normalize(l.Name).StartsWith(query, StringComparison.Ordinal) ? 0 : 1);
    }

    private void Fill()
    {
        _list.Children.Clear();
        foreach (var language in Matches())
        {
            var row = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9, 7),
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new DockPanel
                {
                    Children =
                    {
                        Docked(new TextBlock { Text = language.Native, FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center }),
                        new TextBlock { Text = language.Name, FontSize = 13, Foreground = Brushes.White },
                    },
                },
            };
            row.PointerEntered += (_, _) => row.Background = RowHover;
            row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
            row.Click += (_, _) => Answer(language.Code);
            _list.Children.Add(row);
        }
        if (_list.Children.Count == 0)
            _list.Children.Add(new TextBlock { Text = L.NoMatch, FontSize = 12, Foreground = Muted, Margin = new Thickness(9, 8) });
    }

    private static Control Docked(Control control)
    {
        DockPanel.SetDock(control, Dock.Right);
        return control;
    }

    private void Answer(string code)
    {
        _answer.TrySetResult(code);
        Close();
    }

    /// <summary>Lowercase and without accents, so "Coréano" and "coreano" find the same language.</summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
