using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TopRacerLauncher;

public partial class AdminWindow : Window
{
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _artStatusTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private bool _loaded;
    private bool _artBusy;
    private CancellationTokenSource? _artCts;

    public AdminWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        _uiTimer.Tick += (_, _) => UpdateRemainingLabel();
        _uiTimer.Start();
        _artStatusTimer.Tick += (_, _) =>
        {
            _artStatusTimer.Stop();
            ArtStatusLabel.Text = "";
        };
        Closing += (_, _) =>
        {
            _uiTimer.Stop();
            _artStatusTimer.Stop();
            _artCts?.Cancel();   // не докачиваем обложки в закрытое окно
        };

    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var s = App.Settings;

        try { AutostartCheck.IsChecked = StartupManager.IsEnabled(); }
        catch { AutostartCheck.IsChecked = false; }

        SoundCheck.IsChecked = s.SoundAlerts;
        AutoCoversCheck.IsChecked = s.AutoCovers;
        HideStoreCheck.IsChecked = s.HideStoreWindows;
        KioskCheck.IsChecked = s.KioskMode;
        UserNameBox.Text = s.UserName;
        RefreshPlayStats(DateTime.Today);
        UpdateUrlBox.Text = s.UpdateUrl;
        VersionLabel.Text = "Текущая версия: " + UpdateService.CurrentVersion;

        MinutesSlider.Value = Math.Clamp(s.SessionMinutes, 30, 240);
        MinutesBox.Text = ((int)MinutesSlider.Value).ToString();
        FullScreenCheck.IsChecked = s.FullScreen;

        RefreshGamesList();
        BuildThemeButtons();
        BuildAspectButtons();
        UpdateAppBackgroundPreview();
        UpdateRemainingLabel();
        UpdateKillButton();
        RefreshHistory();
        RefreshUsage();

        _loaded = true;
    }

    // ---------- Фон приложения ----------

    private void BrowseAppBackground_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите картинку фона",
            Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Все файлы (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        SetAppBackground(dlg.FileName);
    }

    private void SearchAppBackground_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CoverSearchWindow(ImageKind.Background, "фон для заставки") { Owner = this };
        if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.ResultPath)) return;

        SetAppBackground(dlg.ResultPath);
    }

    private void ClearAppBackground_Click(object sender, RoutedEventArgs e)
    {
        var old = App.Settings.AppBackgroundPath;
        App.Settings.AppBackgroundPath = null;
        ImageSearchService.DeleteIfMedia(old);
        App.SaveState();
        UpdateAppBackgroundPreview();
    }

    private void SetAppBackground(string path)
    {
        var old = App.Settings.AppBackgroundPath;
        if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase))
            ImageSearchService.DeleteIfMedia(old);   // старый фон из media удаляем

        App.Settings.AppBackgroundPath = path;
        App.SaveState();
        UpdateAppBackgroundPreview();
    }

    private void UpdateAppBackgroundPreview()
    {
        var path = App.Settings.AppBackgroundPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            BgPreviewImage.Source = null;
            BgPreviewImage.Visibility = Visibility.Collapsed;
            BgPreviewHint.Visibility = Visibility.Visible;
            BgPreviewHint.Text = "Фон не задан";
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            BgPreviewImage.Source = bmp;
            BgPreviewImage.Visibility = Visibility.Visible;
            BgPreviewHint.Visibility = Visibility.Collapsed;
        }
        catch
        {
            BgPreviewImage.Source = null;
            BgPreviewImage.Visibility = Visibility.Collapsed;
            BgPreviewHint.Visibility = Visibility.Visible;
            BgPreviewHint.Text = "Не удалось открыть картинку";
        }
    }

    // ---------- Тема оформления ----------

    private void BuildThemeButtons()
    {
        ThemeButtonsPanel.Children.Clear();

        foreach (var theme in Themes.All)
        {
            var btn = new Button
            {
                Tag = theme.Key,
                Width = 130,
                Height = 44,
                Margin = new Thickness(0, 0, 10, 10),
                Content = BuildThemePreview(theme),
                ToolTip = theme.Name,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            btn.Click += Theme_Click;
            ThemeButtonsPanel.Children.Add(btn);
        }

        HighlightActiveTheme();
    }

    private static System.Windows.UIElement BuildThemePreview(ThemeInfo theme)
    {
        var accent = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(theme.Accent)!);
        accent.Freeze();

        var stack = new StackPanel { Orientation = Orientation.Horizontal };

        stack.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            Background = accent,
            CornerRadius = new CornerRadius(6),
        });
        stack.Children.Add(new TextBlock
        {
            Text = theme.Name,
            Foreground = FindResourceSafe("TextBrush"),
            FontSize = 12.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        });

        return stack;
    }

    private static Brush FindResourceSafe(string key)
    {
        if (Application.Current?.TryFindResource(key) is Brush b) return b;
        return Brushes.White;
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string key) return;

        Themes.Apply(key);
        App.Settings.AccentTheme = key;
        App.SaveState();
        HighlightActiveTheme();
    }

    private void HighlightActiveTheme()
    {
        foreach (var child in ThemeButtonsPanel.Children)
        {
            if (child is not Button b || b.Tag is not string key) continue;
            bool active = string.Equals(key, App.Settings.AccentTheme, StringComparison.OrdinalIgnoreCase);

            b.BorderBrush = active
                ? FindResourceSafe("AccentBrush")
                : FindResourceSafe("BorderBrush");
            b.BorderThickness = new Thickness(active ? 2 : 1);
        }
    }

    private void UpdateRemainingLabel()
    {
        var s = App.Settings;

        if (!s.TimerEnabled)
        {
            TimerStateLabel.Text = "Таймер выключен";
            TimerStateLabel.Foreground = FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
            UpdateKillButton();
            return;
        }

        int remaining = App.ComputeRemainingSeconds(s);
        s.RemainingSeconds = remaining;

        if (s.Locked || remaining <= 0)
        {
            TimerStateLabel.Text = "Время истекло";
            TimerStateLabel.Foreground = FindResource("DangerBrush") as Brush ?? Brushes.White;
        }
        else
        {
            var time = TimeSpan.FromSeconds(remaining);
            string formatted = time.TotalHours >= 1
                ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
                : $"{time.Minutes}:{time.Seconds:D2}";
            TimerStateLabel.Text = "Осталось: " + formatted;
            TimerStateLabel.Foreground = FindResource("AccentBrush") as Brush ?? Brushes.White;
        }

        UpdateKillButton();
    }

    // ---------- Таймер: стоп / 30 минут / 1 час / ползунок ----------

    private void TimerStop_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Settings;
        s.TimerEnabled = false;
        s.Locked = false;
        s.TimerPaused = false;
        s.RemainingSeconds = 0;
        s.PausedRemainingSeconds = 0;
        s.SessionEndUtc = default;
        App.SaveState();

        HistoryLog.Add("time_reset", detail: "таймер остановлен");
        UpdateRemainingLabel();
        RefreshHistory();
    }

    private void TimerPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;
        if (!int.TryParse(tag, out int minutes)) return;

        ApplyTimerMinutes(minutes);
    }

    private void ApplyTime_Click(object sender, RoutedEventArgs e)
    {
        SyncBoxToSlider();
        ApplyTimerMinutes((int)MinutesSlider.Value);
    }

    private void ApplyTimerMinutes(int minutes)
    {
        var s = App.Settings;
        s.SessionMinutes = Math.Clamp(minutes, 30, 240);
        s.TimerEnabled = true;
        App.StartNewSession();
        App.SaveState();

        UpdateRemainingLabel();
        RefreshHistory();
    }

    private void MinutesSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MinutesBox == null || MinutesSlider == null) return;
        MinutesBox.Text = ((int)MinutesSlider.Value).ToString();
    }

    private void MinutesBox_LostFocus(object sender, RoutedEventArgs e) => SyncBoxToSlider();

    private void MinutesBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            SyncBoxToSlider();
        }
    }

    private void SyncBoxToSlider()
    {
        if (int.TryParse(MinutesBox.Text.Trim(), out int minutes))
            MinutesSlider.Value = Math.Clamp(minutes, 30, 240);
        MinutesBox.Text = ((int)MinutesSlider.Value).ToString();
    }
    private void UpdateKillButton()
    {
        KillGameButton.IsEnabled = App.Runner.IsRunning;
        KillGameButton.Content = App.Runner.IsRunning
            ? "Завершить запущенную игру"
            : "Игра не запущена";
    }

    // ---------- Игры ----------

    private void GamesList_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GamesList.SelectedItem is not GameItem game) return;

        var dlg = new AddGameWindow(game) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        App.SaveState();
        RefreshGamesList();
    }

    private void RefreshGamesList()
    {
        var selectedId = (GamesList.SelectedItem as GameItem)?.Id;

        GamesList.ItemsSource = null;
        GamesList.ItemsSource = App.Settings.Games;

        if (selectedId != null)
            GamesList.SelectedItem = App.Settings.Games.FirstOrDefault(g => g.Id == selectedId);

        int n = App.Settings.Games.Count;
        GamesCountLabel.Text = n switch
        {
            0 => "нет игр",
            1 => "1 игра",
            < 5 => $"{n} игры",
            _ => $"{n} игр",
        };
    }

    private async void ScanGames_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ScanWindow { Owner = this };
        dlg.ShowDialog();

        if (dlg.SelectedResults == null || dlg.SelectedResults.Count == 0) return;

        var existing = new HashSet<string>(
            App.Settings.Games.Select(g => g.ExecutablePath.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        var added = new List<GameItem>();
        foreach (var r in dlg.SelectedResults)
        {
            if (!existing.Add(r.FilePath.Trim().ToLowerInvariant())) continue;

            var item = new GameItem
            {
                Title = r.Title,
                ExecutablePath = r.FilePath,
                Arguments = r.Arguments,
                AppId = r.AppId,
            };
            App.Settings.Games.Add(item);
            added.Add(item);
        }

        if (added.Count == 0)
        {
            MessageBox.Show(this, "Все выбранные игры уже есть в библиотеке.",
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        HistoryLog.Add("games_added", detail: added.Count.ToString());
        App.SaveState();
        RefreshGamesList();
        RefreshHistory();

        if (App.Settings.AutoCovers)
        {
            await LoadArtForAsync(added);
        }
        else
        {
            SetArtStatus($"Добавлено игр: {added.Count}", autoHide: true);
            MessageBox.Show(this, $"Добавлено игр: {added.Count}",
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ---------- Пропорции карточек ----------

    private void BuildAspectButtons()
    {
        AspectPanel.Children.Clear();

        foreach (var aspect in CardAspects.All)
        {
            var btn = new Button
            {
                Content = aspect.Label,
                Tag = aspect.Key,
                Height = 36,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 8, 8),
                Style = FindResource("BaseButton") as Style,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            btn.Click += Aspect_Click;
            AspectPanel.Children.Add(btn);
        }

        HighlightAspect();
    }

    private void HighlightAspect()
    {
        foreach (var child in AspectPanel.Children)
        {
            if (child is not Button b || b.Tag is not string key) continue;

            bool active = string.Equals(key, App.Settings.CardAspect, StringComparison.OrdinalIgnoreCase);
            b.BorderBrush = active
                ? FindResource("AccentBrush") as Brush ?? Brushes.SteelBlue
                : FindResource("BorderBrush") as Brush ?? Brushes.Gray;
            b.BorderThickness = new Thickness(active ? 2 : 1);
        }
    }

    private void Aspect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string key) return;

        App.Settings.CardAspect = key;
        App.SaveState();
        HighlightAspect();
    }

    // ---------- Автоматические обложки ----------

    private async void FetchMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameItem game)
        {
            MessageBox.Show(this, "Сначала выберите игру в списке.",
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetArtStatus($"Загружаю метаданные: {game.Title}…");

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var meta = await SteamMetadataService.FetchAsync(game.AppId, game.Title, cts.Token);

            if (meta == null)
            {
                SetArtStatus("Не удалось найти игру в Steam", autoHide: true);
                return;
            }

            SteamMetadataService.Apply(game, meta);
            App.SaveState();
            RefreshGamesList();
            HistoryLog.Add("metadata", title: game.Title);
            SetArtStatus("Метаданные загружены", autoHide: true);
        }
        catch (Exception ex)
        {
            SetArtStatus("Ошибка: " + ex.Message, autoHide: true);
        }
    }

    private async void FetchAllMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (_artBusy) return;

        var targets = App.Settings.Games.ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "В библиотеке нет игр.", "TopRacerLauncher",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _artBusy = true;
        int ok = 0;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));

            for (int i = 0; i < targets.Count; i++)
            {
                var game = targets[i];
                SetArtStatus($"Метаданные: {i + 1}/{targets.Count} — {game.Title}");

                try
                {
                    var meta = await SteamMetadataService.FetchAsync(game.AppId, game.Title, cts.Token);
                    if (meta != null)
                    {
                        SteamMetadataService.Apply(game, meta);
                        ok++;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { /* пропускаем игру */ }

                try { await Task.Delay(400, cts.Token); } catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            _artBusy = false;
            App.SaveState();
            RefreshGamesList();
            RefreshHistory();
            SetArtStatus($"Метаданные обновлены: {ok} из {targets.Count}", autoHide: true);
        }
    }

    private async void FetchAllCovers_Click(object sender, RoutedEventArgs e)
    {
        if (_artBusy) return;

        var targets = App.Settings.Games
            .Where(g => string.IsNullOrEmpty(g.CoverPath) || string.IsNullOrEmpty(g.BackgroundPath))
            .ToList();

        if (targets.Count == 0)
        {
            MessageBox.Show(this, "У всех игр уже есть обложки и фоны.",
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await LoadArtForAsync(targets);
    }

    /// <summary>Подтягивает обложки и фоны из Steam для перечисленных игр.</summary>
    private async Task LoadArtForAsync(List<GameItem> games)
    {
        if (_artBusy) return;

        var targets = games
            .Where(g => string.IsNullOrEmpty(g.CoverPath) || string.IsNullOrEmpty(g.BackgroundPath))
            .ToList();

        if (targets.Count == 0) return;

        _artBusy = true;
        SetArtStatus($"Обложки: 0/{targets.Count}…");
        int ok = 0;

        _artCts?.Dispose();
        _artCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
        _artCts.CancelAfter(TimeSpan.FromMinutes(4));

        try
        {
            var ct = _artCts.Token;

            for (int i = 0; i < targets.Count; i++)
            {
                var game = targets[i];
                SetArtStatus($"Обложки: {i + 1}/{targets.Count} — {game.Title}");

                try
                {
                    var art = await ImageSearchService.FetchArtAsync(game.Title, game.AppId, ct);
                    if (art == null) continue;

                    if (art.CoverPath != null && string.IsNullOrEmpty(game.CoverPath))
                        game.CoverPath = art.CoverPath;
                    if (art.BackgroundPath != null && string.IsNullOrEmpty(game.BackgroundPath))
                        game.BackgroundPath = art.BackgroundPath;
                    if (string.IsNullOrEmpty(game.AppId))
                        game.AppId = art.AppId;

                    ok++;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch { /* одна игра не нашлась — идём дальше */ }

                // Заодно подтягиваем метаданные, если их ещё нет.
                if (string.IsNullOrEmpty(game.Description))
                {
                    try
                    {
                        var meta = await SteamMetadataService.FetchAsync(game.AppId, game.Title, ct);
                        if (meta != null) SteamMetadataService.Apply(game, meta);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch { /* метаданные не критичны */ }
                }
            }
        }
        finally
        {
            _artBusy = false;
            App.SaveState();
            RefreshGamesList();
            HistoryLog.Add("covers", detail: $"{ok} из {targets.Count}");
            RefreshHistory();
            SetArtStatus(
                ok > 0 ? $"Обложки подтянуты: {ok} из {targets.Count}" : "Обложки не нашлись — выберите вручную",
                autoHide: true);
        }
    }

    private void SetArtStatus(string text, bool autoHide = false)
    {
        ArtStatusLabel.Text = text;
        _artStatusTimer.Stop();
        if (autoHide) _artStatusTimer.Start();
    }

    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddGameWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.ResultGame == null) return;

        App.Settings.Games.Add(dlg.ResultGame);
        App.SaveState();
        RefreshGamesList();
    }

    private void ChangeCover_Click(object sender, RoutedEventArgs e) => EditImage(ImageKind.Cover);

    private void ChangeBackground_Click(object sender, RoutedEventArgs e) => EditImage(ImageKind.Background);

    private void EditImage(ImageKind kind)
    {
        if (GamesList.SelectedItem is not GameItem game)
        {
            MessageBox.Show(this, "Сначала выберите игру в списке.",
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new CoverSearchWindow(kind, game.Title) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        // null — отмена, "" — убрать картинку, иначе — новый файл
        var oldPath = kind == ImageKind.Cover ? game.CoverPath : game.BackgroundPath;
        var newPath = dlg.ResultPath;

        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            ImageSearchService.DeleteIfMedia(oldPath);

        if (kind == ImageKind.Cover)
            game.CoverPath = string.IsNullOrEmpty(newPath) ? null : newPath;
        else
            game.BackgroundPath = string.IsNullOrEmpty(newPath) ? null : newPath;

        App.SaveState();
        RefreshGamesList();
    }

    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (GamesList.SelectedItem is not GameItem game)
        {
            MessageBox.Show(this, "Сначала выберите игру в списке.",
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(this,
            $"Удалить «{game.Title}» из библиотеки?",
            "TopRacerLauncher", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        App.Settings.Games.Remove(game);
        ImageSearchService.DeleteIfMedia(game.CoverPath);
        ImageSearchService.DeleteIfMedia(game.BackgroundPath);
        App.SaveState();
        RefreshGamesList();
    }

    // ---------- Система и безопасность ----------

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new FirstRunWindow(changeMode: true) { Owner = this };
        dlg.ShowDialog();
    }

    private void Autostart_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        bool enabled = AutostartCheck.IsChecked == true;
        try
        {
            StartupManager.SetEnabled(enabled);
        }
        catch (Exception ex)
        {
            AutostartCheck.IsChecked = !enabled;
            MessageBox.Show(this,
                "Не удалось настроить автозагрузку:\n\n" + ex.Message,
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Sound_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.SoundAlerts = SoundCheck.IsChecked == true;
        App.SaveState();

        if (App.Settings.SoundAlerts) SoundAlert.Info();
    }

    private void SaveUserName_Click(object sender, RoutedEventArgs e)
    {
        var name = UserNameBox.Text.Trim();
        if (name.Length == 0) name = Environment.UserName;

        App.Settings.UserName = name;
        App.SaveState();
        UserNameBox.Text = name;

        SetArtStatus("Имя сохранено: " + name, autoHide: true);
    }

    private async void SaveUpdate_Click(object sender, RoutedEventArgs e)
    {
        var url = UpdateUrlBox.Text.Trim();

        App.Settings.UpdateUrl = url;
        App.SaveState();

        if (url.Length == 0)
        {
            SetArtStatus("Ссылка на обновления очищена", autoHide: true);
            return;
        }

        SetArtStatus("Проверяем обновления…");
        var info = await UpdateService.CheckAsync(url, CancellationToken.None);

        if (info == null)
        {
            SetArtStatus("Обновлений нет (версия " + UpdateService.CurrentVersion + ")", autoHide: true);
            return;
        }

        SetArtStatus("Доступна версия " + info.Version, autoHide: true);
        new UpdateWindow(info, url) { Owner = this }.Show();
    }

    private void Kiosk_Changed(object sender, RoutedEventArgs e)
    {
        bool enabled = KioskCheck.IsChecked == true;

        App.Settings.KioskMode = enabled;
        App.SaveState();

        try { KioskMode.Apply(enabled); } catch { }
        if (Owner is MainWindow main) main.Topmost = enabled;

        SetArtStatus(enabled ? "Киоск-режим включён" : "Киоск-режим выключен", autoHide: true);
    }

    private void HideStore_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.HideStoreWindows = HideStoreCheck.IsChecked == true;
        App.SaveState();
    }

    private void AutoCovers_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.AutoCovers = AutoCoversCheck.IsChecked == true;
        App.SaveState();
    }

    private void FullScreen_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.FullScreen = FullScreenCheck.IsChecked == true;
        App.SaveState();
    }

    private void KillGame_Click(object sender, RoutedEventArgs e)
    {
        bool killed = App.Runner.KillCurrent();
        UpdateKillButton();
        MessageBox.Show(this,
            killed ? "Игра завершена." : "Запущенная игра не найдена (возможно, уже закрыта).",
            "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------- История и статистика ----------

    // ---------- Статистика по играм ----------

    private void RefreshPlayStats(DateTime? since)
    {
        var rows = PlayStats.Aggregate(since);

        PlayStatsList.ItemsSource = rows;
        PlayStatsEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var total = PlayStats.TotalTime(rows);
        PlayStatsTotal.Text = rows.Count == 0
            ? ""
            : $"Всего: {(int)total.TotalHours} ч {total.Minutes:D2} мин";
    }

    private void PlayStatsToday_Click(object sender, RoutedEventArgs e) => RefreshPlayStats(DateTime.Today);

    private void PlayStatsWeek_Click(object sender, RoutedEventArgs e) => RefreshPlayStats(DateTime.Today.AddDays(-6));

    private void PlayStatsAll_Click(object sender, RoutedEventArgs e) => RefreshPlayStats(null);

    private void RefreshHistory()
    {
        var entries = HistoryLog.ReadLast(400);

        // Последние 7 дней: сколько времени выдано и сколько было попыток ввода пароля.
        var today = DateTime.Now.Date;
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6)).ToArray();
        var granted = days.ToDictionary(d => d, _ => 0);
        int fails = 0;

        foreach (var e in entries)
        {
            var day = e.Ts.ToLocalTime().Date;
            if (e.Type is "session_start" or "session_extend" && granted.ContainsKey(day))
                granted[day] += e.Seconds;
            if (day >= days[0] && e.Type is "unlock_fail" or "admin_fail")
                fails++;
        }

        int total = granted.Values.Sum();
        HistorySummary.Text = total > 0
            ? $"за 7 дней: {FormatMinutes(total)}  •  неверных вводов: {fails}"
            : "за 7 дней: —";

        BuildHistoryChart(days, granted);
        HistoryList.ItemsSource = entries.Select(HistoryLog.Describe).ToList();
    }

    // ---------- Использование ----------

    private void RefreshUsage()
    {
        var usage = UsageStats.Read();

        UsageSummaryLabel.Text = $"всего запусков: {usage.TotalLaunches}  •  за 7 дней: {usage.LaunchesLast7Days}";

        UsageStatsPanel.Children.Clear();
        UsageStatsPanel.Children.Add(StatTile(usage.TotalLaunches.ToString(), "запусков"));
        UsageStatsPanel.Children.Add(StatTile(usage.Installs.ToString(), "учётных записей"));
        UsageStatsPanel.Children.Add(StatTile(usage.LaunchesLast7Days.ToString(), "за 7 дней"));

        var lines = new List<string>();
        foreach (var r in usage.Records)
        {
            var last = r.LastSeenUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            lines.Add($"{r.User} @ {r.Machine} — {r.Launches} запусков, последний {last}");
        }

        if (lines.Count == 0) lines.Add("Пока нет данных о запусках.");

        if (usage.FirstSeenUtc.HasValue)
        {
            lines.Add($"Первый запуск: {usage.FirstSeenUtc.Value.ToLocalTime():dd.MM.yyyy HH:mm}");
        }

        UsageList.ItemsSource = lines;
    }

    private Border StatTile(string value, string caption)
    {
        var accent = FindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;
        var muted = FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 26,
            FontWeight = FontWeights.Bold,
            Foreground = accent,
        });
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 12,
            Foreground = muted,
            Margin = new Thickness(0, 2, 0, 0),
        });

        return new Border
        {
            Background = FindResource("BgCardBrush") as Brush ?? Brushes.DimGray,
            BorderBrush = FindResource("BorderBrush") as Brush ?? Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18, 10, 18, 10),
            Margin = new Thickness(0, 0, 10, 10),
            MinWidth = 130,
            Child = stack,
        };
    }

    private void BuildHistoryChart(DateTime[] days, Dictionary<DateTime, int> granted)
    {
        HistoryChart.Children.Clear();

        int max = Math.Max(1, granted.Values.Max());
        var accent = FindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;
        var muted = FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        var text = FindResource("TextBrush") as Brush ?? Brushes.White;
        var names = new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        var today = DateTime.Now.Date;

        foreach (var day in days)
        {
            int seconds = granted[day];
            double height = seconds > 0 ? Math.Max(6, 68.0 * seconds / max) : 3;

            var bar = new Border
            {
                Height = height,
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(5),
                Background = seconds > 0 ? accent : new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x33)),
                Opacity = day == today ? 1.0 : 0.65,
                Margin = new Thickness(6, 0, 6, 0),
            };

            var barArea = new Grid { Height = 72 };
            barArea.Children.Add(bar);

            var column = new StackPanel { Width = 72 };
            column.Children.Add(barArea);
            column.Children.Add(new TextBlock
            {
                Text = names[((int)day.DayOfWeek + 6) % 7],
                FontSize = 11.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = day == today ? text : muted,
                Margin = new Thickness(0, 6, 0, 0),
            });
            column.Children.Add(new TextBlock
            {
                Text = seconds > 0 ? FormatMinutes(seconds) : "—",
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = muted,
            });

            HistoryChart.Children.Add(column);
        }
    }

    private static string FormatMinutes(int seconds)
    {
        if (seconds <= 0) return "—";
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours} ч {t.Minutes} мин"
            : $"{t.Minutes} мин";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
