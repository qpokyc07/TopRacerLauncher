using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>
/// Тост «Достижение открыто»: появляется в углу, не забирает фокус
/// (игру не сворачивает) и сам прячется. Клик открывает полку достижений.
/// </summary>
public partial class AchievementToast : Window
{
    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(9) };

    public AchievementToast(string icon, string title, string body)
    {
        InitializeComponent();

        IconText.Text = string.IsNullOrWhiteSpace(icon) ? "\U0001F3C6" : icon;
        TitleText.Text = title;
        BodyText.Text = body;

        _autoHide.Tick += (_, _) => HideAnimated();
        Loaded += (_, _) =>
        {
            PlaceBottomRight();
            _autoHide.Start();
        };
        Closed += (_, _) => _autoHide.Stop();

        MouseLeftButtonUp += OnClick;
    }

    private void PlaceBottomRight()
    {
        var area = SystemParameters.WorkArea;

        Left = area.Right - ActualWidth - 24;

        // Выше напоминания о времени: они могут появиться одновременно.
        Top = area.Bottom - ActualHeight - 24 - 150;

        if (Left < area.Left) Left = area.Left + 24;
        if (Top < area.Top) Top = area.Top + 24;
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        HideAnimated();

        try
        {
            var window = new AchievementsWindow();
            window.Show();
            window.Activate();
        }
        catch { /* окно уже открыто или приложение закрывается */ }
    }

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
