using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TopRacerLauncher;

public partial class AdminWindow : Window
{
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _artStatusTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer _micChangeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _loaded;
    private bool _artBusy;
    private CancellationTokenSource? _artCts;

    public AdminWindow()
    {
        InitializeComponent();

        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

        // Окно «поднимается» снизу: сдвиг + фейд. Самому Window RenderTransform задавать нельзя.
        var slide = new TranslateTransform(0, 26);
        if (Content is FrameworkElement root) root.RenderTransform = slide;

        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };

        slide.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(
            26, 0, TimeSpan.FromMilliseconds(340)) { EasingFunction = ease });
        Loaded += OnLoaded;
        _uiTimer.Tick += (_, _) => UpdateRemainingLabel();
        _uiTimer.Start();
        _artStatusTimer.Tick += (_, _) =>
        {
            _artStatusTimer.Stop();
            ArtStatusLabel.Text = "";
        };
        _micChangeTimer.Tick += (_, _) =>
        {
            _micChangeTimer.Stop();
            App.SaveState();
        };
        Closing += (_, _) =>
        {
            _uiTimer.Stop();
            _artStatusTimer.Stop();
            _micChangeTimer.Stop();
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
        RefreshPromoList();
        TransparentPanelsCheck.IsChecked = s.TransparentPanels;
        AccentTitlesCheck.IsChecked = s.AccentTitles;
        PcSpecsCheck.IsChecked = s.ShowPcSpecs;
        WelcomeAdCheck.IsChecked = s.WelcomeAdEnabled;
        WelcomeAdTitleBox.Text = s.WelcomeAdTitle;
        WelcomeAdTextBox.Text = s.WelcomeAdText;
        AfishaEnabledCheck.IsChecked = s.AfishaEnabled;
        AfishaTickerCheck.IsChecked = s.AfishaTickerEnabled;
        AfishaTickerBox.Text = s.AfishaTickerText;
        RefreshAfishaList();

        _micDevices = VoiceEngine.GetInputDevices();
        _spkDevices = VoiceEngine.GetOutputDevices();
        MicGainSlider.Value = Math.Clamp(s.VoiceInputGain, 0, 200);
        OpenMicCheck.IsChecked = s.VoiceMode == "open";
        VoiceOverlayCheck.IsChecked = s.VoiceOverlay;
        AutoAddSteamCheck.IsChecked = s.AutoAddSteamGames;
        AutoScanCheck.IsChecked = s.AutoScanGames;
        RefreshMicLabels();
        MakeSectionsCollapsible();
        UpdateUrlBox.Text = s.UpdateUrl;
        VersionLabel.Text = "Текущая версия: " + UpdateService.CurrentVersion;

        MinutesSlider.Value = Math.Clamp(s.SessionMinutes, 30, 240);
        MinutesBox.Text = ((int)MinutesSlider.Value).ToString();
        FullScreenCheck.IsChecked = s.FullScreen;

        RefreshGamesList();
        BuildThemeButtons();
        BuildAspectButtons();

        double cardWidth = s.CardWidth;
        if (cardWidth < 100 || cardWidth > 380) cardWidth = 178;
        CardWidthSlider.Value = cardWidth;
        CardWidthLabel.Text = ((int)cardWidth).ToString();

        UpdateAppBackgroundPreview();
        UpdateRemainingLabel();
        UpdateKillButton();
        RefreshHistory();
        RefreshUsage();

        BuildAdminTabs();
        HighlightTabs(StatsTabs, "history");
        HighlightTabs(ClubTabs, "promo");

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

    /// <summary>Превращает каждую карточку-раздел в сворачиваемую строку.</summary>
    private void MakeSectionsCollapsible()
    {
        if (Content is not DependencyObject root) return;

        var cards = new List<Border>();
        CollectCards(root, cards);

        foreach (var card in cards)
        {
            if (card.Child is not StackPanel content || content.Children.Count == 0) continue;
            if (card.Tag is "collapsible") continue;

            card.Tag = "collapsible";

            // Первый элемент карточки — её заголовок (TextBlock или Grid с заголовком и меткой справа).
            var headerSource = content.Children[0];
            content.Children.RemoveAt(0);

            var body = new StackPanel();
            while (content.Children.Count > 0)
            {
                var child = content.Children[0];
                content.Children.RemoveAt(0);
                body.Children.Add(child);
            }

            var chevron = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M 0,0 L 10,0 L 5,6 Z"),
                Fill = FindResourceSafe("TextSecondaryBrush"),
                Width = 10,
                Height = 6,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(chevron, 0);
            Grid.SetColumn(headerSource, 1);
            headerGrid.Children.Add(chevron);
            headerGrid.Children.Add(headerSource);

            var header = new Button
            {
                Style = FindResource("SectionHeaderButton") as Style,
                Content = headerGrid,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Свернуть / развернуть",
            };

            header.Click += (_, _) =>
            {
                bool collapse = body.Visibility != Visibility.Collapsed;
                body.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
                chevron.RenderTransform = new System.Windows.Media.RotateTransform(collapse ? -90 : 0);
            };

            content.Children.Add(header);
            content.Children.Add(body);
        }
    }

    private static void CollectCards(DependencyObject parent, List<Border> cards)
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

            if (child is Border border && border.Style is not null &&
                border.Style == Application.Current?.TryFindResource("PanelCard"))
            {
                cards.Add(border);
            }

            CollectCards(child, cards);
        }
    }

    /// <summary>
    /// Плитки тем: каждая — мини-окно лаунчера (фон темы + акцентная полоса),
    /// поэтому темы видно сразу и не нужно ничего прокручивать.
    /// </summary>
    private void BuildThemeButtons()
    {
        ThemeButtonsPanel.Children.Clear();

        foreach (var theme in Themes.All)
        {
            var accentBrush = MakeBrush(theme.Accent, theme.AccentHover);
            var accentColor = (Color)ColorConverter.ConvertFromString(theme.Accent)!;

            var preview = new Grid();

            // Фон «окна» — как у самой темы.
            preview.Children.Add(new Border { Background = MakeWindowPreview(theme) });

            // Верхняя световая кромка.
            preview.Children.Add(new Border
            {
                Height = 14,
                VerticalAlignment = VerticalAlignment.Top,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                    new System.Windows.Point(0, 0), new System.Windows.Point(0, 1)),
            });

            // Акцентная полоса — цвет темы (крупно, чтобы темы не путались).
            preview.Children.Add(new Border
            {
                Height = 26,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = accentBrush,
            });

            // Мягкий блик на акцентной полосе.
            preview.Children.Add(new Border
            {
                Height = 26,
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new LinearGradientBrush(
                    Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
                    new System.Windows.Point(0, 0), new System.Windows.Point(0, 1)),
            });

            // Точка акцента в углу.
            preview.Children.Add(new Border
            {
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(5),
                Background = accentBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(8, 7, 0, 0),
            });

            var chip = new Border
            {
                Tag = theme.Key,
                Width = 88,
                Height = 64,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromArgb(0x40, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = theme.Name,
                Child = preview,
            };

            // Плавный подъём при наведении.
            chip.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            chip.RenderTransform = new ScaleTransform(1, 1);
            chip.MouseEnter += (_, _) => AnimateChip(chip, 1.05);
            chip.MouseLeave += (_, _) => AnimateChip(chip, 1.0);
            chip.MouseLeftButtonUp += ThemeChip_Click;

            ThemeButtonsPanel.Children.Add(chip);

            // Подпись под плиткой не нужна: название в подсказке и под полем.
            chip.ToolTip = theme.Name + " — нажмите, чтобы применить";
        }

        HighlightActiveTheme();
    }

    private static void AnimateChip(Border chip, double scale)
    {
        if (chip.RenderTransform is not ScaleTransform transform) return;

        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };

        transform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(scale, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
        transform.BeginAnimation(ScaleTransform.ScaleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(scale, TimeSpan.FromMilliseconds(140)) { EasingFunction = ease });
    }

    /// <summary>Фон мини-окна: тёмный цвет темы, у градиентных — переход к насыщенному.</summary>
    private static Brush MakeWindowPreview(ThemeInfo theme)
    {
        if (ColorConverter.ConvertFromString(theme.BgWindow) is not Color from)
            return new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x22));

        if (!string.IsNullOrWhiteSpace(theme.BgWindow2) &&
            ColorConverter.ConvertFromString(theme.BgWindow2) is Color mid)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 1),
            };

            gradient.GradientStops.Add(new GradientStop(from, 0));
            gradient.GradientStops.Add(new GradientStop(mid, 0.6));

            if (!string.IsNullOrWhiteSpace(theme.BgWindow3) &&
                ColorConverter.ConvertFromString(theme.BgWindow3) is Color third)
            {
                gradient.GradientStops.Add(new GradientStop(third, 1));
            }

            gradient.Freeze();
            return gradient;
        }

        var solid = new SolidColorBrush(from);
        solid.Freeze();
        return solid;
    }

    /// <summary>Цвет темы: для градиентных — плавный переход от акцента ко второму цвету.</summary>
    private static Brush MakeBrush(string accentHex, string? secondHex)
    {
        var accent = (Color)ColorConverter.ConvertFromString(accentHex)!;

        Brush brush;

        if (!string.IsNullOrWhiteSpace(secondHex) &&
            ColorConverter.ConvertFromString(secondHex) is Color second)
        {
            brush = new LinearGradientBrush(accent, second, new System.Windows.Point(0, 0),
                new System.Windows.Point(1, 1));
        }
        else
        {
            brush = new SolidColorBrush(accent);
        }

        brush.Freeze();
        return brush;
    }
    private static Brush FindResourceSafe(string key)
    {
        if (Application.Current?.TryFindResource(key) is Brush b) return b;
        return Brushes.White;
    }

    private void ThemeChip_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border chip || chip.Tag is not string key) return;

        Themes.Apply(key);
        App.Settings.AccentTheme = key;
        App.SaveState();
        HighlightActiveTheme();
    }

    private void HighlightActiveTheme()
    {
        var activeKey = App.Settings.AccentTheme;
        var activeTheme = Themes.Find(activeKey);

        ThemeNameText.Text = "Выбрана: " + activeTheme.Name;

        foreach (var child in ThemeButtonsPanel.Children)
        {
            if (child is not Border chip || chip.Tag is not string key) continue;

            bool active = string.Equals(key, activeKey, StringComparison.OrdinalIgnoreCase);

            chip.BorderBrush = active
                ? (FindResource("AccentBrush") as Brush ?? Brushes.White)
                : Brushes.Transparent;
            chip.BorderThickness = new Thickness(active ? 2.5 : 2);
            chip.Opacity = active ? 1.0 : 0.82;
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
    {        AspectPanel.Children.Clear();

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

    // ---------- Размер карточек ----------

    private void CardWidth_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded) return;

        int width = (int)Math.Round(CardWidthSlider.Value);
        CardWidthLabel.Text = width.ToString();
        App.Settings.CardWidth = width;
    }

    /// <summary>Пишем конфиг, когда ползунок отпустили (а не на каждый пиксель).</summary>
    private void CardWidth_Commit(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        App.SaveState();
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

        SteamLibraryWatcher.Forget(game);
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

    private void RefreshPromoList()
    {
        PromoList.ItemsSource = null;
        PromoList.ItemsSource = App.Settings.PromoCodes.ToList();
    }

    private void CreatePromo_Click(object sender, RoutedEventArgs e)
    {
        int minutes = int.TryParse(PromoMinutesBox.Text.Trim(), out int m) ? m : 30;
        int uses = int.TryParse(PromoUsesBox.Text.Trim(), out int u) ? u : 1;
        int days = int.TryParse(PromoDaysBox.Text.Trim(), out int d) ? d : 30;

        if (!PromoService.TryCreate(PromoCodeBox.Text, minutes, uses, days, out var promo, out string error))
        {
            NewPromoText.Text = error;
            NewPromoText.Foreground = FindResource("DangerBrush") as Brush ?? Brushes.OrangeRed;
            NewPromoText.Visibility = Visibility.Visible;
            return;
        }

        PromoCodeBox.Clear();

        var until = promo!.ExpiresUtc.HasValue
            ? "  •  действует до " + promo.ExpiresUtc.Value.ToLocalTime().ToString("dd.MM.yyyy")
            : "  •  без срока";

        NewPromoText.Text = "Код: " + promo.Code + "  •  " + promo.Minutes + " мин" + until;
        NewPromoText.Foreground = FindResource("AccentBrush") as Brush ?? Brushes.White;
        NewPromoText.Visibility = Visibility.Visible;

        RefreshPromoList();
    }

    private void DeletePromo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string code) return;

        App.Settings.PromoCodes.RemoveAll(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
        App.SaveState();
        RefreshPromoList();
    }

    private void Panels_Changed(object sender, RoutedEventArgs e)
    {
        App.Settings.TransparentPanels = TransparentPanelsCheck.IsChecked == true;
        App.SaveState();
    }

    private void Titles_Changed(object sender, RoutedEventArgs e)
    {
        App.Settings.AccentTitles = AccentTitlesCheck.IsChecked == true;
        App.SaveState();
    }

    /// <summary>Скрыть/показать блок «Характеристики ПК» на главном экране.</summary>
    private void PcSpecs_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.ShowPcSpecs = PcSpecsCheck.IsChecked == true;
        App.SaveState();
    }

    // ---------- Реклама на экране приветствия ----------

    private void WelcomeAd_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.WelcomeAdEnabled = WelcomeAdCheck.IsChecked == true;
        App.SaveState();

        if (Owner is MainWindow main) main.RefreshWelcomeAd();
    }

    private void SaveWelcomeAd_Click(object sender, RoutedEventArgs e)
    {
        var title = WelcomeAdTitleBox.Text.Trim();
        var text = WelcomeAdTextBox.Text.Trim();

        if (title.Length == 0 && text.Length == 0)
        {
            SetWelcomeAdStatus("Заполните заголовок или текст поста.", error: true);
            return;
        }

        App.Settings.WelcomeAdTitle = title;
        App.Settings.WelcomeAdText = text;
        App.SaveState();

        if (Owner is MainWindow main) main.RefreshWelcomeAd();

        SetWelcomeAdStatus("Сохранено — текст уже на экране приветствия.", error: false);
    }

    private void SetWelcomeAdStatus(string message, bool error)
    {
        WelcomeAdStatusText.Text = message;
        WelcomeAdStatusText.Visibility = Visibility.Visible;
        WelcomeAdStatusText.Foreground = error
            ? FindResource("DangerBrush") as Brush ?? Brushes.OrangeRed
            : FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
    }

    // ---------- Вкладки админ-меню ----------

    private readonly Dictionary<string, UIElement> _adminPages = new();

    /// <summary>Вкладки разделов: заголовок + короткое пояснение, как в браузере.</summary>
    private void BuildAdminTabs()
    {
        AdminTabs.Children.Clear();
        _adminPages.Clear();

        var tabs = new (string Key, string Title, string Hint, UIElement Page)[]
        {
            ("session", "Таймер и игры", "сессия, минуты, библиотека", PageSession),
            ("stats",   "Статистика",    "история и часы по играм",       PageStats),
            ("club",    "Клуб",          "промокоды, посты, афиша",              PageClub),
            ("comm",    "Связь",         "микрофон, звонки, оверлей",            PageComm),
            ("system",  "Система",       "пароль, оформление, обновления", PageSystem),
        };

        foreach (var (key, title, hint, page) in tabs)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            content.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 10,
                Margin = new Thickness(0, 1, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
            });

            var button = new Button
            {
                Content = content,
                Tag = key,
                Padding = new Thickness(14, 6, 14, 7),
                Margin = new Thickness(0, 0, 8, 0),
                Style = FindResource("BaseButton") as Style,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            button.Click += AdminTab_Click;

            AdminTabs.Children.Add(button);
            _adminPages[key] = page;
        }

        SelectAdminTab("session");
    }

    private void AdminTab_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string key) SelectAdminTab(key);
    }

    private void SelectAdminTab(string key)
    {
        if (_adminPages.Count == 0) return;

        foreach (var (pageKey, page) in _adminPages)
            page.Visibility = pageKey == key ? Visibility.Visible : Visibility.Collapsed;

        var accent = FindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;
        var card = FindResource("BgCardBrush") as Brush ?? Brushes.DimGray;
        var secondary = FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        var activeHint = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));

        foreach (var child in AdminTabs.Children)
        {
            if (child is not Button button || button.Tag is not string tag) continue;

            bool active = tag == key;
            button.Background = active ? accent : card;
            button.BorderBrush = active ? accent : (FindResource("BorderBrush") as Brush ?? Brushes.Gray);

            if (button.Content is StackPanel content && content.Children.Count >= 2)
            {
                if (content.Children[0] is TextBlock title)
                    title.Foreground = active ? Brushes.White : (FindResource("TextBrush") as Brush ?? Brushes.White);
                if (content.Children[1] is TextBlock hint)
                    hint.Foreground = active ? activeHint : secondary;
            }
        }

        AdminScroll?.ScrollToTop();
    }
    // ---------- Переключатели разделов ----------

    private void StatsTab_Click(object sender, RoutedEventArgs e)
        => SelectTab(sender, StatsTabs, ("history", StatsPageHistory), ("usage", StatsPageUsage), ("games", StatsPageGames));

    private void ClubTab_Click(object sender, RoutedEventArgs e)
        => SelectTab(sender, ClubTabs, ("promo", ClubPagePromo), ("posts", ClubPagePosts), ("afisha", ClubPageAfisha));

    private void SelectTab(object? sender, StackPanel tabs, params (string Tag, UIElement Page)[] pages)
    {
        string tag = (sender as FrameworkElement)?.Tag as string ?? pages[0].Tag;

        foreach (var (pageTag, page) in pages)
            page.Visibility = pageTag == tag ? Visibility.Visible : Visibility.Collapsed;

        HighlightTabs(tabs, tag);
    }

    /// <summary>Подсвечивает активную вкладку, остальные оставляет обычными.</summary>
    private void HighlightTabs(StackPanel tabs, string activeTag)
    {
        var accent = FindResource("AccentBrush") as Brush ?? Brushes.SteelBlue;
        var card = FindResource("BgCardBrush") as Brush ?? Brushes.DimGray;
        var text = FindResource("TextBrush") as Brush ?? Brushes.White;
        var border = FindResource("BorderBrush") as Brush ?? Brushes.Gray;

        foreach (var child in tabs.Children)
        {
            if (child is not Button button) continue;

            bool active = (button.Tag as string) == activeTag;
            button.Background = active ? accent : card;
            button.Foreground = active ? Brushes.White : text;
            button.BorderBrush = active ? accent : border;
            button.BorderThickness = new Thickness(1);
        }
    }

    // ---------- Микрофон и звонки ----------

    private List<string> _micDevices = new();
    private List<string> _spkDevices = new();

    private void RefreshMicLabels()
    {
        MicDeviceButton.Content = DeviceText("Микрофон", App.Settings.VoiceInputDevice);
        SpkDeviceButton.Content = DeviceText("Наушники", App.Settings.VoiceOutputDevice);
        MicGainLabel.Text = "Громкость микрофона: " + App.Settings.VoiceInputGain + "%";

        var key = System.Windows.Input.KeyInterop.KeyFromVirtualKey(App.Settings.VoiceKey);
        PttKeyButton.Content = "Клавиша: " + (key == Key.None ? "V" : key.ToString());
    }

    private bool _capturingPtt;

    private void PttKey_Click(object sender, RoutedEventArgs e)
    {
        _capturingPtt = true;
        PttKeyButton.Content = "Нажмите клавишу…";
        PttKeyButton.Focus();
        Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!_capturingPtt)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _capturingPtt = false;
            RefreshMicLabels();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        int virtualKey = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey <= 0) return;

        App.Settings.VoiceKey = virtualKey;
        VoiceEngine.VoiceKey = virtualKey;
        PttHook.Key = virtualKey;
        App.SaveState();

        _capturingPtt = false;
        RefreshMicLabels();
    }

    private static TextBlock DeviceText(string label, string device) => new()
    {
        Text = label + ": " + (string.IsNullOrWhiteSpace(device) ? "по умолчанию" : device) + "  ▾",
        TextTrimming = TextTrimming.CharacterEllipsis,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 12.5,
    };

    private void MicDevice_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = MicDeviceButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        AddDeviceMenuItem(menu, "Системный по умолчанию", "", App.Settings.VoiceInputDevice, value =>
        {
            App.Settings.VoiceInputDevice = value;
            VoiceEngine.InputDeviceName = value;
        });

        foreach (var name in _micDevices)
        {
            var device = name;
            AddDeviceMenuItem(menu, device, device, App.Settings.VoiceInputDevice, value =>
            {
                App.Settings.VoiceInputDevice = value;
                VoiceEngine.InputDeviceName = value;
            });
        }

        menu.IsOpen = true;
    }

    private void SpkDevice_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = SpkDeviceButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };

        AddDeviceMenuItem(menu, "Системное по умолчанию", "", App.Settings.VoiceOutputDevice, value =>
        {
            App.Settings.VoiceOutputDevice = value;
            VoiceEngine.OutputDeviceName = value;
        });

        foreach (var name in _spkDevices)
        {
            var device = name;
            AddDeviceMenuItem(menu, device, device, App.Settings.VoiceOutputDevice, value =>
            {
                App.Settings.VoiceOutputDevice = value;
                VoiceEngine.OutputDeviceName = value;
            });
        }

        menu.IsOpen = true;
    }

    private void AddDeviceMenuItem(ContextMenu menu, string header, string value,
        string currentValue, Action<string> apply)
    {
        var item = new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = string.Equals(currentValue, value, StringComparison.OrdinalIgnoreCase),
        };

        item.Click += (_, _) =>
        {
            apply(value);
            App.SaveState();
            RefreshMicLabels();
            RestartVoiceIfInCall();
        };

        menu.Items.Add(item);
    }

    /// <summary>Смена устройства на ходу: перезапускаем звонок, чтобы звук пошёл через новое устройство.</summary>
    private static void RestartVoiceIfInCall()
    {
        if (!VoiceEngine.InCall) return;

        var room = VoiceEngine.RoomId;
        VoiceEngine.LeaveCall();
        VoiceEngine.StartCall(room);
    }

    private void AutoScan_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.AutoScanGames = AutoScanCheck.IsChecked == true;
        App.SaveState();

        if (App.Settings.AutoScanGames)
        {
            SetArtStatus("Ищу новые игры…", autoHide: false);
            _ = SteamLibraryWatcher.ScanAllAsync().ContinueWith(_ =>
                Dispatcher.BeginInvoke(new Action(() => SetArtStatus("Поиск игр завершён.", autoHide: true))));
        }
        else
        {
            SetArtStatus("Автопоиск игр при запуске выключен.", autoHide: true);
        }
    }

    private void AutoAddSteam_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.AutoAddSteamGames = AutoAddSteamCheck.IsChecked == true;
        App.SaveState();

        if (App.Settings.AutoAddSteamGames) SteamLibraryWatcher.Restart();
        else SteamLibraryWatcher.Stop();
    }

    private void VoiceOverlay_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.VoiceOverlay = VoiceOverlayCheck.IsChecked == true;
        App.SaveState();
        VoiceOverlayWindow.Update();
    }

    private void MicMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        bool open = OpenMicCheck.IsChecked == true;
        App.Settings.VoiceMode = open ? "open" : "ptt";
        VoiceEngine.OpenMic = open;
        App.SaveState();
    }

    private void MicGain_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_loaded) return;

        int gain = (int)Math.Round(MicGainSlider.Value);
        App.Settings.VoiceInputGain = gain;
        VoiceEngine.InputGain = gain;
        MicGainLabel.Text = "Громкость микрофона: " + gain + "%";

        // Пишем конфиг после того, как пользователь отпустил ползунок.
        _micChangeTimer.Stop();
        _micChangeTimer.Start();
    }

    // ---------- Афиша клуба ----------

    private string? _editingAfishaId;
    private string? _afishaFormImagePath;

    private void OpenChangelog_Click(object sender, RoutedEventArgs e)
        => ChangelogWindow.Open(this);

    private void Afisha_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.AfishaEnabled = AfishaEnabledCheck.IsChecked == true;
        App.SaveState();

        if (Owner is MainWindow main) main.RefreshAfisha();
    }

    private void AfishaTicker_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;

        App.Settings.AfishaTickerEnabled = AfishaTickerCheck.IsChecked == true;
        App.SaveState();

        if (Owner is MainWindow main) main.RefreshAfisha();
    }

    private void SaveAfishaTicker_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.AfishaTickerText = AfishaTickerBox.Text.Trim();
        App.SaveState();

        if (Owner is MainWindow main) main.RefreshAfisha();

        SetAfishaStatus("Анонс сохранён — он уже в верхней панели.", error: false);
    }

    private void PickAfishaImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Фото для афиши",
            Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Все файлы (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        // Копия в media: картинка не пропадёт, если исходную папку удалят.
        _afishaFormImagePath = TryImportToMedia(dlg.FileName) ?? dlg.FileName;
        UpdateAfishaImagePreview();
        SetAfishaStatus("Фото добавлено. Не забудьте сохранить событие.", error: false);
    }

    private void ClearAfishaImage_Click(object sender, RoutedEventArgs e)
    {
        _afishaFormImagePath = null;
        UpdateAfishaImagePreview();
    }

    private void SaveAfishaItem_Click(object sender, RoutedEventArgs e)
    {
        var title = AfishaTitleBox.Text.Trim();
        var text = AfishaTextBox.Text.Trim();

        if (title.Length == 0 && text.Length == 0)
        {
            SetAfishaStatus("Заполните заголовок или текст события.", error: true);
            return;
        }

        var items = App.Settings.Afisha;
        bool wasEditing = _editingAfishaId != null;
        var existing = items.FirstOrDefault(i => i.Id == _editingAfishaId);

        if (existing != null)
        {
            if (!string.Equals(existing.ImagePath, _afishaFormImagePath, StringComparison.OrdinalIgnoreCase))
                ImageSearchService.DeleteIfMedia(existing.ImagePath);

            existing.Title = title;
            existing.Text = text;
            existing.DateText = AfishaDateBox.Text.Trim();
            existing.ImagePath = _afishaFormImagePath;
        }
        else
        {
            items.Add(new AfishaItem
            {
                Title = title,
                Text = text,
                DateText = AfishaDateBox.Text.Trim(),
                ImagePath = _afishaFormImagePath,
            });
        }

        App.SaveState();
        ResetAfishaForm();
        RefreshAfishaList();

        if (Owner is MainWindow main) main.RefreshAfisha();

        SetAfishaStatus(wasEditing ? "Событие обновлено." : "Событие добавлено в афишу.", error: false);
    }

    private void CancelAfishaEdit_Click(object sender, RoutedEventArgs e)
    {
        ResetAfishaForm();
        SetAfishaStatus("Редактирование отменено.", error: false);
    }

    private void ResetAfishaForm()
    {
        _editingAfishaId = null;
        _afishaFormImagePath = null;
        AfishaTitleBox.Text = "";
        AfishaDateBox.Text = "";
        AfishaTextBox.Text = "";
        AfishaFormTitle.Text = "Новое событие";
        AfishaSaveButton.Content = "Добавить в афишу";
        AfishaCancelButton.Visibility = Visibility.Collapsed;
        UpdateAfishaImagePreview();
    }

    private void UpdateAfishaImagePreview()
    {
        var image = ImageCache.Load(_afishaFormImagePath, 320);

        AfishaImagePreview.Source = image;
        AfishaImagePreview.Visibility = image == null ? Visibility.Collapsed : Visibility.Visible;
        AfishaImageHint.Visibility = image == null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EditAfishaItem(AfishaItem item)
    {
        _editingAfishaId = item.Id;
        _afishaFormImagePath = item.ImagePath;

        AfishaTitleBox.Text = item.Title;
        AfishaDateBox.Text = item.DateText;
        AfishaTextBox.Text = item.Text;

        AfishaFormTitle.Text = "Редактирование события";
        AfishaSaveButton.Content = "Сохранить изменения";
        AfishaCancelButton.Visibility = Visibility.Visible;

        UpdateAfishaImagePreview();
        SetAfishaStatus("Измените поля и нажмите «Сохранить изменения».", error: false);
        AfishaTitleBox.Focus();
    }

    private void DeleteAfishaItem(AfishaItem item)
    {
        ImageSearchService.DeleteIfMedia(item.ImagePath);
        App.Settings.Afisha.Remove(item);
        App.SaveState();

        if (_editingAfishaId == item.Id) ResetAfishaForm();

        RefreshAfishaList();
        if (Owner is MainWindow main) main.RefreshAfisha();

        SetAfishaStatus("Событие удалено.", error: false);
    }

    private void RefreshAfishaList()
    {
        AfishaListPanel.Children.Clear();

        var items = App.Settings.Afisha;
        if (items.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Пока пусто. Добавьте событие — карточка появится на главном экране.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            AfishaListPanel.Children.Add(empty);
            return;
        }

        foreach (var item in items)
        {
            var row = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                BorderThickness = new Thickness(1),
            };
            row.SetResourceReference(Border.BackgroundProperty, "BgCardBrush");
            row.SetResourceReference(Border.BorderBrushProperty, "GlassBorderBrush");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var thumb = new Border
            {
                Width = 64,
                Height = 40,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 10, 0),
                ClipToBounds = true,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x19)),
            };

            var thumbImage = ImageCache.Load(item.ImagePath, 160);
            thumb.Child = thumbImage != null
                ? new Image { Source = thumbImage, Stretch = Stretch.UniformToFill }
                : new TextBlock
                {
                    Text = "фото",
                    FontSize = 10,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Brushes.Gray,
                };
            Grid.SetColumn(thumb, 0);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var title = new TextBlock
            {
                Text = (item.Enabled ? "" : "скрыто · ") +
                       (string.IsNullOrWhiteSpace(item.Title) ? "Без названия" : item.Title.Trim()),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            textStack.Children.Add(title);

            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(item.DateText)) bits.Add(item.DateText.Trim());
            if (!string.IsNullOrWhiteSpace(item.Text)) bits.Add(item.Text.Trim());

            var subtitle = string.Join("  ·  ", bits);
            if (subtitle.Length > 80) subtitle = subtitle[..79] + "…";

            if (subtitle.Length > 0)
            {
                var subText = new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                subText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
                textStack.Children.Add(subText);
            }

            Grid.SetColumn(textStack, 1);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };

            var captured = item;

            var visibility = new Button
            {
                Content = item.Enabled ? "Скрыть" : "Показать",
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 6, 0),
                Style = FindResource("BaseButton") as Style,
            };
            visibility.Click += (_, _) =>
            {
                captured.Enabled = !captured.Enabled;
                App.SaveState();
                RefreshAfishaList();
                if (Owner is MainWindow main) main.RefreshAfisha();
            };

            var edit = new Button
            {
                Content = "Изменить",
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11.5,
                Margin = new Thickness(0, 0, 6, 0),
                Style = FindResource("BaseButton") as Style,
            };
            edit.Click += (_, _) => EditAfishaItem(captured);

            var delete = new Button
            {
                Content = "Удалить",
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11.5,
                Style = FindResource("DangerButton") as Style,
            };
            delete.Click += (_, _) => DeleteAfishaItem(captured);

            buttons.Children.Add(visibility);
            buttons.Children.Add(edit);
            buttons.Children.Add(delete);
            Grid.SetColumn(buttons, 2);

            grid.Children.Add(thumb);
            grid.Children.Add(textStack);
            grid.Children.Add(buttons);
            row.Child = grid;
            AfishaListPanel.Children.Add(row);
        }
    }

    private void SetAfishaStatus(string message, bool error)
    {
        AfishaStatusText.Text = message;
        AfishaStatusText.Visibility = Visibility.Visible;
        AfishaStatusText.Foreground = error
            ? FindResource("DangerBrush") as Brush ?? Brushes.OrangeRed
            : FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
    }

    /// <summary>Копирует выбранное фото в media (чтобы афиша не зависела от исходной папки).</summary>
    private static string? TryImportToMedia(string source)
    {
        try
        {
            var directory = ImageSearchService.MediaDirectory;
            Directory.CreateDirectory(directory);

            var extension = Path.GetExtension(source);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";

            var target = Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
            File.Copy(source, target, overwrite: false);
            return target;
        }
        catch
        {
            return null;
        }
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
