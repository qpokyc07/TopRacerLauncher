using System.Threading;
using System.Windows;

namespace TopRacerLauncher;

/// <summary>Окно «Доступно обновление»: изменения, загрузка и перезапуск.</summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _info;
    private readonly string _source;

    public UpdateWindow(UpdateInfo info, string source)
    {
        InitializeComponent();

        _info = info;
        _source = source;

        VersionText.Text = "Доступна версия " + info.Version;
        CurrentText.Text = "У вас установлена " + UpdateService.CurrentVersion;
        NotesText.Text = string.IsNullOrWhiteSpace(info.Notes)
            ? "Список изменений не указан."
            : info.Notes;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        InstallButton.Content = "Загрузка…";

        var progress = new Progress<int>(value => Progress.Value = value);
        bool ok = await UpdateService.DownloadAsync(_info, _source, progress, CancellationToken.None);

        if (!ok)
        {
            Progress.Visibility = Visibility.Collapsed;
            InstallButton.Content = "Загрузить и обновить";
            InstallButton.IsEnabled = true;
            LaterButton.IsEnabled = true;

            MessageBox.Show(this,
                "Не удалось скачать обновление. Проверьте интернет или ссылку в настройках.",
                "Обновление", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        InstallButton.Content = "Устанавливаем…";

        if (!UpdateService.ApplyAndRestart())
        {
            MessageBox.Show(this,
                "Обновление скачано, но применить его не удалось.\n" +
                "Файл лежит в папке:" + Environment.NewLine +
                System.IO.Path.GetDirectoryName(UpdateService.PendingFile ?? ""),
                "Обновление", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ((App)Application.Current).Shutdown();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
