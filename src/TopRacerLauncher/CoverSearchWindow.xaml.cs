using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TopRacerLauncher;

/// <summary>Карточка результата поиска картинки.</summary>
public sealed class SearchResultVm : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; init; } = "";
    public ImageSource? Thumb { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public ImageSearchItem? Item { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class CoverSearchWindow : Window
{
    private readonly ImageKind _kind;
    private List<SearchResultVm> _results = new();
    private SearchResultVm? _selected;
    private bool _scanning;
    private CancellationTokenSource? _downloadCts;

    /// <summary>
    /// null  — отмена;
    /// ""    — убрать картинку;
    /// путь  — использовать этот файл.
    /// </summary>
    public string? ResultPath { get; private set; }

    public CoverSearchWindow(ImageKind kind, string suggestedQuery)
    {
        InitializeComponent();
        _kind = kind;

        TitleText.Text = kind == ImageKind.Cover
            ? "Обложка игры — поиск в интернете"
            : "Задний фон игры — поиск в интернете";

        QueryBox.Text = suggestedQuery ?? "";
        Closing += (_, _) => _downloadCts?.Cancel();   // не пишем результат в закрытое окно
        Loaded += (_, _) => { if (QueryBox.Text.Trim().Length > 0) Search_Click(this, new RoutedEventArgs()); };
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text.Trim();
        if (query.Length == 0)
        {
            StatusText.Text = "Введите название игры.";
            return;
        }

        SetBusy(true);
        StatusText.Text = "Ищу «" + query + "» в интернете…";
        _selected = null;
        ApplyButton.IsEnabled = false;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));

            var items = await ImageSearchService.SearchAsync(query, cts.Token);

            // Превью грузим параллельно и асинхронно: раньше 12 картинок качались
            // синхронно на UI-потоке и окно подвисало на несколько секунд.
            var tasks = items.Select(async it => new SearchResultVm
            {
                Name = it.Name,
                Thumb = await ImageSearchService.LoadThumbnailAsync(it.ThumbnailUrl, 200, cts.Token),
                Item = it,
            });

            _results = (await Task.WhenAll(tasks)).ToList();

            ResultsItems.ItemsSource = _results;
            ResultsScroll.Visibility = _results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            StatusText.Text = _results.Count > 0
                ? "Найдено: " + _results.Count + ". Нажмите на картинку, затем «Использовать выбранное»."
                : "Ничего не найдено. Попробуйте другое название (можно на английском).";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Истекло время ожидания интернета. Проверьте подключение.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка поиска: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not SearchResultVm vm) return;

        foreach (var r in _results) r.IsSelected = false;
        vm.IsSelected = true;
        _selected = vm;
        ApplyButton.IsEnabled = true;

        // Двойной клик — сразу применить выбранную картинку.
        if (e.ClickCount == 2 && !_scanning)
            Apply_Click(sender, new RoutedEventArgs());
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Item == null) return;

        var item = _selected.Item;
        SetBusy(true);
        StatusText.Text = "Скачиваю картинку…";

        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var path = await ImageSearchService.DownloadAsync(item, _kind, _downloadCts.Token);

            if (!IsVisible)
            {
                // Окно закрыли, пока шла загрузка: не оставляем мусор и не трогаем DialogResult.
                try { File.Delete(path); } catch { /* не критично */ }
                return;
            }

            ResultPath = path;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            if (IsVisible) { StatusText.Text = "Загрузка отменена."; SetBusy(false); }
        }
        catch (Exception ex)
        {
            if (IsVisible) { StatusText.Text = "Не удалось скачать: " + ex.Message; SetBusy(false); }
        }
    }

    private void BrowseLocal_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = _kind == ImageKind.Cover ? "Выберите обложку" : "Выберите задний фон",
            Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        ResultPath = dlg.FileName;
        DialogResult = true;
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        ResultPath = "";
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Search_Click(sender, new RoutedEventArgs());
        }
    }

    private void SetBusy(bool busy)
    {
        _scanning = busy;
        SearchButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy && _selected != null;
        RemoveButton.IsEnabled = !busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.IsIndeterminate = busy;
    }
}
