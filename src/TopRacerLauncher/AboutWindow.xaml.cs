using System.Diagnostics;
using System.Windows;

namespace TopRacerLauncher;

/// <summary>О приложении: для кого сделано, кому принадлежат права, кто автор.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = typeof(AboutWindow).Assembly.GetName().Version;
        VersionText.Text = version == null
            ? ""
            : "версия " + version.Major + "." + version.Minor + "." + version.Build;
    }

    private void Telegram_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://t.me/qpokycnow") { UseShellExecute = true });
        }
        catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
