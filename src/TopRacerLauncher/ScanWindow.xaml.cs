using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TopRacerLauncher;

/// <summary>Строка в списке найденных игр.</summary>
public sealed class ScanItemVm : INotifyPropertyChanged
{
    private bool _isChecked;

    public string Title { get; init; } = "";
    public string Path { get; init; } = "";
    public string Source { get; init; } = "";
    public ScanResult? Result { get; init; }

    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class ScanWindow : Window
{
    private List<ScanItemVm> _all = new();
    private List<ScanItemVm> _visible = new();
    private bool _scanning;

    /// <summary>Выбранные пользователем игры (null — отмена).</summary>
    public List<ScanResult>? SelectedResults { get; private set; }

    public ScanWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => StartScan();
    }

    // ------------------------------------------------------------- Скан

    private void StartScan()
    {
        if (_scanning) return;
        _scanning = true;

        SetBusy(true, "Сканирую установленные игры…");
        _all = new List<ScanItemVm>();
        _visible = new List<ScanItemVm>();
        GamesItems.ItemsSource = _visible;

        Task.Run(() =>
        {
            List<ScanResult> found;
            try
            {
                found = GameScanner.Scan(status =>
                    Dispatcher.BeginInvoke(new Action(() => StatusText.Text = status + "…")));
            }
            catch (Exception ex)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    StatusText.Text = "Ошибка сканирования: " + ex.Message;
                    SetBusy(false);
                }));
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _all = found.Select(f => new ScanItemVm
                {
                    Title = f.Title,
                    Path = f.FilePath,
                    Source = f.Source,
                    Result = f,
                    // Ярлыки с рабочего стола отмечены сразу — это то, что видит пользователь.
                    IsChecked = f.Source.StartsWith("Ярлык", StringComparison.Ordinal),
                }).ToList();

                ApplyFilter();
                SetBusy(false);

                StatusText.Text = _all.Count > 0
                    ? "Найдено: " + _all.Count + ". Ярлыки с рабочего стола уже отмечены — проверьте и нажмите «Добавить выбранные»."
                    : "Игры не найдены. Проверьте, где установлена игра, или добавьте вручную.";
            }));
        });
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _scanning = busy;
        Progress.IsIndeterminate = busy;
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        RescanButton.IsEnabled = !busy;
        AddButton.IsEnabled = !busy;
        SelectAllButton.IsEnabled = !busy;
        SelectNoneButton.IsEnabled = !busy;
        if (status != null) StatusText.Text = status;
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => StartScan();

    // ------------------------------------------------------- Фильтр

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (_all == null) return;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = FilterBox.Text.Trim();

        _visible = q.Length == 0
            ? _all
            : _all.Where(i =>
                i.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.Path.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();

        GamesItems.ItemsSource = _visible;
        EmptyText.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateAddButtonText();
    }

    // ------------------------------------------------- Выделение

    private void Item_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ScanItemVm vm) return;
        vm.IsChecked = !vm.IsChecked;
        UpdateAddButtonText();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in _visible) i.IsChecked = true;
        UpdateAddButtonText();
    }

    private void SelectNone_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in _visible) i.IsChecked = false;
        UpdateAddButtonText();
    }

    private void UpdateAddButtonText()
    {
        int n = _all.Count(i => i.IsChecked);
        AddButton.Content = n > 0 ? $"Добавить выбранные ({n})" : "Добавить выбранные";
        AddButton.IsEnabled = n > 0 && !_scanning;
    }

    // ------------------------------------------------------- Кнопки

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        SelectedResults = _all.Where(i => i.IsChecked && i.Result != null)
            .Select(i => i.Result!)
            .ToList();

        if (SelectedResults.Count == 0) return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
