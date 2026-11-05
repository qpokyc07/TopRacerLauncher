using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TopRacerLauncher;

/// <summary>
/// Настройка оверлея для одной игры: клавиши выбираются кликом по виртуальной клавиатуре,
/// там же размер и положение оверлея.
/// </summary>
public partial class ControlsSetupWindow : Window
{
    private readonly GameItem _game;
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);

    // (подпись, ключ, ширина)
    private static readonly (string Label, string Key, double Width)[][] Layout =
    {
        new[]
        {
            ("Esc", "ESC", 40.0), ("1", "1", 34.0), ("2", "2", 34.0), ("3", "3", 34.0), ("4", "4", 34.0),
            ("5", "5", 34.0), ("6", "6", 34.0), ("7", "7", 34.0), ("8", "8", 34.0), ("9", "9", 34.0),
            ("0", "0", 34.0), ("Tab", "TAB", 46.0),
        },
        new[]
        {
            ("Q", "Q", 34.0), ("W", "W", 34.0), ("E", "E", 34.0), ("R", "R", 34.0), ("T", "T", 34.0),
            ("Y", "Y", 34.0), ("U", "U", 34.0), ("I", "I", 34.0), ("O", "O", 34.0), ("P", "P", 34.0),
            ("F", "F", 34.0), ("G", "G", 34.0),
        },
        new[]
        {
            ("A", "A", 34.0), ("S", "S", 34.0), ("D", "D", 34.0), ("H", "H", 34.0), ("J", "J", 34.0),
            ("K", "K", 34.0), ("L", "L", 34.0), ("Z", "Z", 34.0), ("X", "X", 34.0), ("C", "C", 34.0),
            ("V", "V", 34.0), ("B", "B", 34.0),
        },
        new[]
        {
            ("Shift", "SHIFT", 70.0), ("Ctrl", "CTRL", 60.0), ("Alt", "ALT", 48.0), ("Пробел", "SPACE", 130.0),
            ("Enter", "ENTER", 62.0), ("N", "N", 34.0), ("M", "M", 34.0),
        },
        new[]
        {
            ("↑", "↑", 44.0), ("←", "←", 44.0), ("↓", "↓", 44.0), ("→", "→", 44.0),
            ("ЛКМ", "ЛКМ", 60.0), ("ПКМ", "ПКМ", 60.0), ("Колесо", "КОЛЕСО", 66.0),
        },
    };

    public ControlsSetupWindow(GameItem game)
    {
        InitializeComponent();

        _game = game;

        HeaderText.Text = "Оверлей: " + game.Title;

        foreach (var key in GameControls.ParseKeys(game.Controls))
            _selected.Add(key);

        BuildKeyboard();
        UpdatePreview();

        ScaleSlider.Value = Math.Clamp(App.Settings.OverlayScale, 60, 140);
        UpdateScaleLabel();

        HintText.Text = "Если клавиши не выбраны — оверлей возьмёт набор по жанру игры. Ctrl+3 в игре включает и выключает оверлей.";
    }

    private void BuildKeyboard()
    {
        KeyboardPanel.Children.Clear();

        foreach (var row in Layout)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

            foreach (var (label, key, width) in row)
            {
                var button = new Button
                {
                    Content = label,
                    Tag = key,
                    MinWidth = width,
                    Height = 34,
                    Padding = new Thickness(6, 0, 6, 0),
                    Margin = new Thickness(0, 0, 5, 0),
                    FontSize = 11.5,
                    Style = FindResource("BaseButton") as Style,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };

                button.Click += Key_Click;
                line.Children.Add(button);
            }

            KeyboardPanel.Children.Add(line);
        }

        RepaintKeys();
    }

    private void RepaintKeys()
    {
        var accent = FindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;

        foreach (var line in KeyboardPanel.Children.OfType<StackPanel>())
        {
            foreach (var child in line.Children.OfType<Button>())
            {
                bool on = child.Tag is string key && _selected.Contains(key);
                child.Background = on ? accent : (FindResource("BgCardBrush") as Brush ?? Brushes.DimGray);
                child.Foreground = on
                    ? (FindResource("AccentTextBrush") as Brush ?? Brushes.White)
                    : (FindResource("TextBrush") as Brush ?? Brushes.White);
            }
        }
    }

    private void Key_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key) return;

        if (!_selected.Remove(key)) _selected.Add(key);

        RepaintKeys();
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var keys = _selected.Select(GameControls.Label).ToList();

        PreviewText.Text = keys.Count == 0
            ? "Клавиши не выбраны — набор по жанру."
            : "Подсветим: " + string.Join("  ", keys);
    }

    private void Scale_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateScaleLabel();

    private void UpdateScaleLabel()
    {
        if (ScaleLabel != null) ScaleLabel.Text = (int)Math.Round(ScaleSlider.Value) + "%";
    }

    private void Position_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string position) return;

        App.Settings.OverlayPosition = position;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _selected.Clear();
        RepaintKeys();
        UpdatePreview();
    }

    private void Test_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.OverlayScale = (int)Math.Round(ScaleSlider.Value);

        var temp = new GameItem { Title = _game.Title, Controls = string.Join(",", _selected) };
        ControlsOverlayWindow.Show(temp.Title, temp.Controls);

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ControlsOverlayWindow.Hide();
        };
        timer.Start();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _game.Controls = string.Join(",", _selected);
        App.Settings.OverlayScale = (int)Math.Round(ScaleSlider.Value);
        App.SaveState();

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
