using System.Windows;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>
/// Табличка «Игра запускается, ждите…» поверх экрана: пока Steam/Rockstar
/// тихо поднимают игру, пользователь видит понятный статус, а не пустой рабочий стол.
/// </summary>
public partial class LaunchOverlayWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(10) };

    public LaunchOverlayWindow(string gameTitle)
    {
        InitializeComponent();

        TitleText.Text = "Игра запускается";
        HintText.Text = string.IsNullOrWhiteSpace(gameTitle)
            ? "ждите…"
            : "«" + gameTitle + "» — ждите…";

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + area.Height * 0.12;
            _timer.Start();
        };
        _timer.Tick += (_, _) => Close();
        Closed += (_, _) => _timer.Stop();
    }

    private void Close_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Close();
}
