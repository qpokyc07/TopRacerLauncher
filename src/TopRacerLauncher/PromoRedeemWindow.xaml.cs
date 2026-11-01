using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TopRacerLauncher;

/// <summary>Ввод промокода: даёт дополнительные минуты сессии.</summary>
public partial class PromoRedeemWindow : Window
{
    public PromoRedeemWindow()
    {
        InitializeComponent();

        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        Loaded += (_, _) => CodeBox.Focus();
    }

    private void CodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Redeem();
    }

    private void Redeem_Click(object sender, RoutedEventArgs e) => Redeem();

    private void Redeem()
    {
        if (!PromoService.TryRedeem(CodeBox.Text, out int minutes, out string error))
        {
            ShowMessage(error, isError: true);
            return;
        }

        var settings = App.Settings;
        settings.TimerEnabled = true;

        if (settings.Locked || App.ComputeRemainingSeconds() <= 0)
        {
            settings.SessionMinutes = minutes;
            settings.Locked = false;
            settings.TimerPaused = false;
            App.StartNewSession();
        }
        else
        {
            App.ExtendSession(minutes);
        }

        App.SaveState();

        ShowMessage($"Готово! Добавлено {minutes} минут.", isError: false);
        CodeBox.Clear();
    }

    private void ShowMessage(string text, bool isError)
    {
        MessageText.Text = text;
        MessageText.Foreground = FindResource(isError ? "DangerBrush" : "AccentBrush") as Brush
                                 ?? Brushes.White;
        MessageText.Visibility = Visibility.Visible;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
