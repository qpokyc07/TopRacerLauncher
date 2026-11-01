using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>
/// Ненавязчивое напоминание поверх всех окон: «осталось N минут» + кнопка продления.
/// Не забирает фокус, чтобы не сворачивать игру, и само прячется через 15 секунд.
/// </summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(15) };

    /// <summary>Пользователь нажал «+15 минут».</summary>
    public event EventHandler? ExtendRequested;

    public ToastWindow(int minutesLeft, bool critical = false)
    {
        InitializeComponent();

        TitleText.Text = minutesLeft <= 1
            ? "Осталась 1 минута"
            : $"Осталось {minutesLeft} минут";

        BodyText.Text = critical
            ? "Сейчас игра закроется. Сохраните прогресс или продлите сеанс."
            : "Лаунчер закроет игру по таймеру. Сохраните прогресс заранее.";

        if (critical)
        {
            Stripe.Background = new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x45));
            IconText.Text = "\u26A0";   // ⚠
        }

        _autoHide.Tick += (_, _) => HideAnimated();
        Loaded += (_, _) =>
        {
            PlaceBottomRight();
            _autoHide.Start();
        };
        Closed += (_, _) => _autoHide.Stop();
    }

    private void PlaceBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 24;
        Top = area.Bottom - ActualHeight - 24;

        // На всякий случай не уезжаем за край экрана.
        if (Left < area.Left) Left = area.Left + 24;
        if (Top < area.Top) Top = area.Top + 24;
    }

    private void Extend_Click(object sender, RoutedEventArgs e)
    {
        // Сначала убираем напоминание, потом спрашиваем пароль: тост «поверх всех»
        // иначе перекроет диалог.
        HideAnimated();
        ExtendRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => HideAnimated();

    private void HideAnimated()
    {
        _autoHide.Stop();
        try
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }
        catch
        {
            Close();
        }
    }
}
