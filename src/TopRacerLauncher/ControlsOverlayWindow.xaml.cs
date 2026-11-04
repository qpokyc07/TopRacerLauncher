using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace TopRacerLauncher;

/// <summary>
/// Полупрозрачный оверлей поверх игры: полная клавиатура и мышь, где подсвечены
/// клавиши, которые используются в игре. Клики проходят сквозь окно, фокус не забирается.
/// </summary>
public partial class ControlsOverlayWindow : Window
{
    private static ControlsOverlayWindow? _instance;

    /// <summary>Отладочный снимок: окно остаётся за экраном и не позиционируется.</summary>
    internal static bool SuppressAutoPlace;

    // Полная раскладка: (подпись, каноническое имя, ширина).
    private static readonly (string Label, string Key, double Width)[][] Rows =
    {
        new[]
        {
            ("Esc", "ESC", 38.0), ("1", "1", 34.0), ("2", "2", 34.0), ("3", "3", 34.0), ("4", "4", 34.0),
            ("5", "5", 34.0), ("6", "6", 34.0), ("7", "7", 34.0), ("8", "8", 34.0), ("9", "9", 34.0),
            ("0", "0", 34.0), ("–", "-", 34.0), ("=", "=", 34.0), ("Backspace", "BACKSPACE", 72.0),
        },
        new[]
        {
            ("Tab", "TAB", 54.0), ("Q", "Q", 100.0), ("W", "W", 0.0), ("E", "E", 0.0), ("R", "R", 0.0),
            ("T", "T", 0.0), ("Y", "Y", 0.0), ("U", "U", 0.0), ("I", "I", 0.0), ("O", "O", 0.0),
            ("P", "P", 0.0), ("[", "[", 0.0), ("]", "]", 0.0), ("\\", "\\", 54.0),
        },
        new[]
        {
            ("Caps", "CAPS", 64.0), ("A", "A", 0.0), ("S", "S", 0.0), ("D", "D", 0.0), ("F", "F", 0.0),
            ("G", "G", 0.0), ("H", "H", 0.0), ("J", "J", 0.0), ("K", "K", 0.0), ("L", "L", 0.0),
            (";", ";", 0.0), ("'", "'", 0.0), ("Enter", "ENTER", 90.0),
        },
        new[]
        {
            ("Shift", "SHIFT", 88.0), ("Z", "Z", 0.0), ("X", "X", 0.0), ("C", "C", 0.0), ("V", "V", 0.0),
            ("B", "B", 0.0), ("N", "N", 0.0), ("M", "M", 0.0), (",", ",", 0.0), (".", ".", 0.0),
            ("/", "/", 0.0), ("Shift", "SHIFT", 88.0),
        },
        new[]
        {
            ("Ctrl", "CTRL", 62.0), ("Win", "WIN", 46.0), ("Alt", "ALT", 46.0), ("Пробел", "SPACE", 220.0),
            ("Alt", "ALT", 46.0), ("Fn", "FN", 46.0), ("Ctrl", "CTRL", 62.0),
        },
    };

    public ControlsOverlayWindow(string gameTitle, IReadOnlySet<string> used, string note)
    {
        InitializeComponent();

        HeaderText.Text = "УПРАВЛЕНИЕ";
        GameText.Text = gameTitle;
        NoteText.Text = note;

        BuildKeyboard(used);

        Loaded += (_, _) =>
        {
            MakeClickThrough();
            if (!SuppressAutoPlace) PlaceAtBottom();
        };
    }

    /// <summary>Показать оверлей управления для игры (или обновить, если уже открыт).</summary>
    public static void Show(string gameTitle)
    {
        var hint = GameControls.For(gameTitle);
        var used = GameControls.UsedKeys(gameTitle);

        if (_instance != null)
        {
            _instance.Close();
            _instance = null;
        }

        _instance = new ControlsOverlayWindow(gameTitle, used, hint.Note);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }

    /// <summary>Спрятать оверлей (игра закрылась или настройку отключили).</summary>
    public static void Hide()
    {
        if (_instance == null) return;

        _instance.Close();
        _instance = null;
    }

    public static bool IsVisible => _instance != null;

    private void BuildKeyboard(IReadOnlySet<string> used)
    {
        var accent = TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6));
        var accentColor = (accent as SolidColorBrush)?.Color ?? Color.FromRgb(0x8B, 0x5C, 0xF6);

        var activeFill = new SolidColorBrush(Color.FromArgb(0xCC, accentColor.R, accentColor.G, accentColor.B));
        var activeEdge = new SolidColorBrush(Color.FromArgb(0xFF, accentColor.R, accentColor.G, accentColor.B));
        var idleFill = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
        var idleEdge = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

        foreach (var brush in new[] { activeFill, activeEdge, idleFill, idleEdge })
            brush.Freeze();

        KeyboardPanel.Children.Clear();

        foreach (var row in Rows)
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 5),
            };

            foreach (var (label, key, width) in row)
            {
                bool hot = used.Contains(key);

                var cap = new Border
                {
                    MinWidth = width > 0 ? width : 34,
                    Height = 34,
                    Margin = new Thickness(0, 0, 5, 0),
                    Padding = new Thickness(6, 0, 6, 0),
                    CornerRadius = new CornerRadius(6),
                    Background = hot ? activeFill : idleFill,
                    BorderBrush = hot ? activeEdge : idleEdge,
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = label,
                        FontSize = 11,
                        FontWeight = hot ? FontWeights.SemiBold : FontWeights.Normal,
                        Foreground = hot ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };

                line.Children.Add(cap);
            }

            KeyboardPanel.Children.Add(line);
        }

        // Правая колонка: стрелки и мышь.
        var extras = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0),
        };

        extras.Children.Add(MakeGroup(new[] { ("↑", "↑") }, used, activeFill, activeEdge, idleFill, idleEdge, 40));
        extras.Children.Add(MakeGroup(new[] { ("←", "←"), ("↓", "↓"), ("→", "→") }, used, activeFill, activeEdge, idleFill, idleEdge, 40));

        var mouse = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0) };
        mouse.Children.Add(MakeCap("ЛКМ", used.Contains("ЛКМ"), activeFill, activeEdge, idleFill, idleEdge, 52));
        mouse.Children.Add(MakeCap("ПКМ", used.Contains("ПКМ"), activeFill, activeEdge, idleFill, idleEdge, 52));
        mouse.Children.Add(MakeCap("Колесо", used.Contains("КОЛЕСО"), activeFill, activeEdge, idleFill, idleEdge, 58));
        extras.Children.Add(mouse);

        KeyboardPanel.Children.Add(extras);
    }

    private static UIElement MakeGroup((string Label, string Key)[] keys, IReadOnlySet<string> used,
        Brush activeFill, Brush activeEdge, Brush idleFill, Brush idleEdge, double width)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };

        foreach (var (label, key) in keys)
            panel.Children.Add(MakeCap(label, used.Contains(key), activeFill, activeEdge, idleFill, idleEdge, width));

        return panel;
    }

    private static Border MakeCap(string label, bool hot, Brush activeFill, Brush activeEdge,
        Brush idleFill, Brush idleEdge, double width)
    {
        return new Border
        {
            MinWidth = width,
            Height = 34,
            Margin = new Thickness(0, 0, 5, 0),
            Padding = new Thickness(6, 0, 6, 0),
            CornerRadius = new CornerRadius(6),
            Background = hot ? activeFill : idleFill,
            BorderBrush = hot ? activeEdge : idleEdge,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = hot ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = hot ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    /// <summary>Ставит оверлей по центру снизу, над панелью задач.</summary>
    private void PlaceAtBottom()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + area.Height - ActualHeight - 36;
    }

    /// <summary>Клики проходят сквозь окно, фокус остаётся у игры.</summary>
    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        int styles = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, styles | WsExTransparent | WsExNoActivate | WsExToolWindow);
    }

    // ---- Win32: клики сквозь окно ----

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
