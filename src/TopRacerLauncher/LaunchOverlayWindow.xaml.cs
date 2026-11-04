using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>
/// Табличка «Игра запускается, ждите…» поверх экрана: пока Steam/Rockstar
/// тихо поднимают игру, пользователь видит понятный статус, а не пустой рабочий стол.
/// Внизу — подсказка управления: мини-клавиатура с нужными клавишами.
/// </summary>
public partial class LaunchOverlayWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(12) };

    /// <summary>Отладочный скриншот: окно не переезжает в центр экрана и не закрывается само.</summary>
    internal static bool SuppressAutoCenter;

    public LaunchOverlayWindow(string gameTitle) : this(gameTitle, null)
    {
    }

    public LaunchOverlayWindow(string gameTitle, GameControls.Hint? controls)
    {
        InitializeComponent();

        TitleText.Text = "Игра запускается";
        HintText.Text = string.IsNullOrWhiteSpace(gameTitle)
            ? "ждите…"
            : "«" + gameTitle + "» — ждите…";

        if (controls != null) BuildControls(controls);

        Loaded += (_, _) =>
        {
            if (SuppressAutoCenter) return;

            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + (area.Height - ActualHeight) / 2;
            _timer.Start();
        };
        _timer.Tick += (_, _) => Close();
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Рисует ряды клавиш: колпачки с подсветкой цветом темы.</summary>
    private void BuildControls(GameControls.Hint hint)
    {
        ControlsPanel.Visibility = Visibility.Visible;
        ControlsNoteText.Text = hint.Note;

        var accent = TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6));
        var accentColor = (accent as SolidColorBrush)?.Color ?? Color.FromRgb(0x8B, 0x5C, 0xF6);

        var fill = new SolidColorBrush(Color.FromArgb(0x33, accentColor.R, accentColor.G, accentColor.B));
        var edge = new SolidColorBrush(Color.FromArgb(0x99, accentColor.R, accentColor.G, accentColor.B));
        fill.Freeze();
        edge.Freeze();

        ControlsRows.Children.Clear();

        foreach (var row in hint.Rows)
        {
            var line = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
            };

            foreach (var key in row)
            {
                line.Children.Add(MakeKeyCap(key, GameControls.Label(key), GameControls.IsWide(key), fill, edge));
            }

            ControlsRows.Children.Add(line);
        }
    }

    private static Border MakeKeyCap(string key, string label, bool wide, Brush fill, Brush edge)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var cap = new Border
        {
            MinWidth = wide ? 62 : 38,
            Height = 34,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(7),
            Background = fill,
            BorderBrush = edge,
            BorderThickness = new Thickness(1),
            Child = text,
            ToolTip = key,
        };

        // Тень-«объём» колпачка.
        cap.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 6,
            ShadowDepth = 2,
            Opacity = 0.35,
        };

        return cap;
    }

    private void Close_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Close();
}
