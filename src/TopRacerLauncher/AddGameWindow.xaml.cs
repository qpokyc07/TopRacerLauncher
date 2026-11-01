using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TopRacerLauncher;

public partial class AddGameWindow : Window
{
    /// <summary>Игра, добавленная пользователем (null, если отмена).</summary>
    public GameItem? ResultGame { get; private set; }

    /// <summary>Режим редактирования: не null — правим существующую игру.</summary>
    private readonly GameItem? _editItem;

    public AddGameWindow(GameItem? editItem = null)
    {
        InitializeComponent();
        _editItem = editItem;

        if (editItem != null)
        {
            HeaderText.Text = "Настройка игры";
            SaveButton.Content = "Сохранить";
            TitleBox.Text = editItem.Title;
            PathBox.Text = editItem.ExecutablePath;
            if (!string.IsNullOrEmpty(editItem.CoverPath))
            {
                CoverBox.Text = editItem.CoverPath;
                ShowPreview(editItem.CoverPath);
            }
        }

        Loaded += (_, _) => TitleBox.Focus();
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите файл игры",
            Filter = "Игры (*.exe;*.lnk;*.bat)|*.exe;*.lnk;*.bat|Все файлы (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        PathBox.Text = dlg.FileName;
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            var name = Path.GetFileNameWithoutExtension(dlg.FileName);
            TitleBox.Text = name;
        }
        HideError();
    }

    private void BrowseCover_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите обложку",
            Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        CoverBox.Text = dlg.FileName;
        ShowPreview(dlg.FileName);
        HideError();
    }

    private void ShowPreview(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            PreviewImage.Source = bmp;
            PreviewImage.Visibility = Visibility.Visible;
            PreviewHint.Visibility = Visibility.Collapsed;
        }
        catch
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewHint.Visibility = Visibility.Visible;
            PreviewHint.Text = "Не удалось открыть изображение — будет заглушка.";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();

        var title = TitleBox.Text.Trim();
        var path = PathBox.Text.Trim();

        if (title.Length == 0)
        {
            ShowError("Укажите название игры.");
            TitleBox.Focus();
            return;
        }
        if (path.Length == 0)
        {
            ShowError("Выберите файл игры (.exe или .lnk).");
            return;
        }

        // Ссылки вида steam://rungameid/… тоже допустимы (их даёт скан ярлыков).
        bool isUrl = path.Contains("://", StringComparison.Ordinal);
        if (!isUrl && !File.Exists(path))
        {
            ShowError("Файл не найден:\n" + path);
            return;
        }

        var newCover = string.IsNullOrWhiteSpace(CoverBox.Text) ? null : CoverBox.Text.Trim();

        if (_editItem != null)
        {
            // Режим редактирования: обновляем существующую игру.
            var oldCover = _editItem.CoverPath;
            _editItem.Title = title;
            _editItem.ExecutablePath = path;

            if (!string.Equals(oldCover, newCover, StringComparison.OrdinalIgnoreCase))
            {
                ImageSearchService.DeleteIfMedia(oldCover);
                _editItem.CoverPath = newCover;
            }

            ResultGame = _editItem;
        }
        else
        {
            ResultGame = new GameItem
            {
                Title = title,
                ExecutablePath = path,
                CoverPath = newCover,
            };
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;
}
