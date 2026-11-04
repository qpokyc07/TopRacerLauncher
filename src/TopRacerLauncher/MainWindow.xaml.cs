using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TopRacerLauncher;

public partial class MainWindow : Window
{
    private static readonly Brush WarnBrush = Freeze(Color.FromRgb(0xE8, 0xA3, 0x3D));

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _cardWidthSave = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private int _saveCounter;
    private bool _shuttingDown;
    private bool _forceClose;

    /// <summary>Открыто модальное админ-меню: таймер идёт, но UI и экран блокировки не трогаем.</summary>
    private bool _adminOpen;

    // Напоминания о времени: показываем один раз на порог.
    private bool _warned5;
    private bool _warned1;
    private ToastWindow? _toast;

    /// <summary>Открытая полка достижений (одна на всё приложение).</summary>
    private AchievementsWindow? _achievementsWindow;

    private static Brush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Состояние UI (размер карточек) с уведомлениями о изменениях.</summary>
    public UiState Ui { get; } = new();

    /// <summary>Разрешение на сворачивание (после ввода пароля).</summary>
    private bool _allowMinimize;

    /// <summary>Игра, показанная в карточке справа.</summary>
    private GameViewModel? _detailVm;

    private readonly DispatcherTimer _friendsTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _friendsSignature = "";

    private readonly Dictionary<string, int> _unread = new();
    private readonly Dictionary<string, DateTime> _typing = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = Ui;

        // Размер карточек из настроек (миграция со старого формата S/M/L/XL).
        ApplyCardSettings();

        // Характеристики ПК по умолчанию свёрнуты: описание игры читается без прокрутки.
        _pcInfoCollapsed = true;
        PcInfoBody.Visibility = Visibility.Collapsed;
        PcInfoChevron.RenderTransform = new RotateTransform(-90);
        PcInfoChevron.RenderTransformOrigin = new Point(0.5, 0.5);

        // Афиша клуба и анонс турнира в верхней панели.
        RefreshAfisha();
        TickerViewport.SizeChanged += (_, _) => StartTickerMarquee();

        // Оверлей звонка поверх игры: показываем и обновляем вместе с состоянием звонка.
        VoiceEngine.StateChanged += VoiceOverlayWindow.Update;

        // Новая игра появилась в Steam — перестраиваем библиотеку.
        SteamLibraryWatcher.LibraryGrown += OnLibraryGrown;

        _tick.Tick += OnTick;
        Achievements.Unlocked += OnAchievementsUnlocked;
        _friendsTimer.Tick += (_, _) => RefreshFriends();
        _cardWidthSave.Tick += (_, _) =>
        {
            _cardWidthSave.Stop();
            App.Settings.CardWidth = (int)Ui.CardWidth;
            App.SaveState();
        };

        Loaded += (_, _) =>
        {
            UpdateLockIndicator();

            if (App.Settings.FullScreen)
                WindowState = WindowState.Maximized;

            RebuildLibrary();
            LoadAppBackground();
            CleanupOrphanMedia();
            RefreshUi();
        ApplyPanelSettings();
        LoadPcInfo();
            _tick.Start();

            // Клуб: киоск-режим и экран приветствия.
            try
            {
                KioskMode.Apply(App.Settings.KioskMode);
                if (App.Settings.KioskMode) Topmost = true;
            }
            catch { }

            ShowWelcome();

            // Метаданные подтягиваются сами, в фоне.
            _ = FillMissingMetadataAsync();

            _ = CheckUpdatesAsync();

            // Достижения могли «дозреть», пока лаунчер был закрыт: считаем после отрисовки.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { Achievements.Evaluate(); } catch { /* не критично */ }
            }), DispatcherPriority.Background);

            // Поиск друзей в локальной сети.
            try { LanPresence.Start(); } catch { }
            LanPresence.CallInvited += OnCallInvited;
            LanPresence.MessageReceived += OnChatMessage;
            LanPresence.TypingReceived += OnTyping;
            RefreshFriends();
            _friendsTimer.Start();
        };
        Activated += (_, _) => { if (!_adminOpen) RefreshUi(); };
        StateChanged += OnWindowStateChanged;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;

        RefreshUi();
        ApplyPanelSettings();
        LoadPcInfo();
    }

    /// <summary>Главное окно плавно поднимается снизу при запуске.</summary>
    private void AnimateWindowEntrance()
    {
        if (Content is not FrameworkElement root) return;

        var shift = new TranslateTransform(0, 30);
        root.RenderTransform = shift;

        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };

        shift.BeginAnimation(TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(30, 0, TimeSpan.FromMilliseconds(460))
            {
                EasingFunction = ease,
            });

        root.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(360))
            {
                EasingFunction = ease,
            });
    }

    // ---------- Библиотека ----------

    private void RebuildLibrary()
    {
        App.Library.Items.Clear();

        // Наигранное время: по нему же сортируем — популярные игры поднимаются наверх.
        var playSeconds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var row in PlayStats.Aggregate(null))
                playSeconds[row.Title] = row.Seconds;
        }
        catch { /* статистика не критична */ }

        var games = new List<(GameItem Game, int Index, int Seconds)>();
        for (int i = 0; i < App.Settings.Games.Count; i++)
        {
            var game = App.Settings.Games[i];

            // Скрытые игры видны только в админ-режиме (зелёный замок).
            if (game.Locked && !App.AdminUnlocked) continue;

            int seconds = game.Title != null && playSeconds.TryGetValue(game.Title.Trim(), out int value)
                ? value
                : 0;

            games.Add((game, i, seconds));
        }

        foreach (var entry in games
                     .OrderByDescending(g => g.Seconds > 0)
                     .ThenByDescending(g => g.Seconds)
                     .ThenBy(g => g.Index))
        {
            App.Library.Items.Add(new GameViewModel(entry.Game, entry.Seconds));
        }

        GamesItems.ItemsSource = App.Library.Items;

        // Плавное появление сетки после перестроения.
        GamesItems.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            0.86, 1, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        });
        EmptyState.Visibility = App.Library.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailsPanel.Visibility = App.Library.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShowDetails(App.Library.Items.FirstOrDefault());
        EmptyTitle.Text = "Библиотека пуста";
        EmptyHint.Text = "Добавьте игры в админ-меню: Ctrl+1";

        int count = App.Library.Items.Count;
        LibraryCountText.Text = count switch
        {
            0 => "пусто",
            1 => "1 игра",
            < 5 => $"{count} игры",
            _ => $"{count} игр",
        };
    }

    /// <summary>Удаляет файлы картинок, на которые больше никто не ссылается.</summary>
    private static void CleanupOrphanMedia()
    {
        try
        {
            var keep = new List<string>();
            foreach (var g in App.Settings.Games)
            {
                if (!string.IsNullOrEmpty(g.CoverPath)) keep.Add(g.CoverPath);
                if (!string.IsNullOrEmpty(g.BackgroundPath)) keep.Add(g.BackgroundPath);
            }
            if (!string.IsNullOrEmpty(App.Settings.AppBackgroundPath))
                keep.Add(App.Settings.AppBackgroundPath);

            // Картинки афиши тоже не считаем мусором.
            foreach (var item in App.Settings.Afisha)
            {
                if (!string.IsNullOrEmpty(item.ImagePath)) keep.Add(item.ImagePath);
            }

            ImageSearchService.CleanupOrphans(keep);
        }
        catch { /* уборка не критична */ }
    }

    // ---------- Таймер ----------

    private void OnTick(object? sender, EventArgs e)
    {
        // Игра закрылась сама — фиксируем длительность. Для Steam-игр смотрим
        // процесс самой игры: оболочка steam.exe завершается сразу после запуска.
        try
        {
            if (App.Runner.HasSessionEnded()) App.Runner.CompleteSession();
        }
        catch { }

        var s = App.Settings;

        // Ограничение по времени выключено: играем без таймера и блокировок.
        if (!s.TimerEnabled)
        {
            if (!_adminOpen) UpdateGameStatus();
            return;
        }

        if (s.Locked) return;

        // Под модальным админ-меню не показываем экран блокировки: он окажется под ним.
        // Время при этом идёт (дедлайн абсолютный), просто UI обновится по возвращении.
        if (_adminOpen) return;

        int remaining = App.ComputeRemainingSeconds();
        bool changed = remaining != s.RemainingSeconds;
        s.RemainingSeconds = remaining;

        if (changed)
        {
            RefreshTimerUi();

            if (++_saveCounter % 10 == 0)
                App.SaveState();
        }

        if (remaining <= 0)
        {
            ExpireSession();
            return;
        }

        CheckTimeWarnings(remaining);
        UpdateGameStatus();
    }

    /// <summary>Напоминания за 5 и 1 минуту до конца сеанса.</summary>
    private void CheckTimeWarnings(int remaining)
    {
        if (remaining > 360)
        {
            _warned5 = _warned1 = false;
            return;
        }

        if (remaining <= 60 && !_warned1)
        {
            _warned1 = true;
            ShowWarningToast(1, critical: true);
            SoundAlert.Warn();
        }
        else if (remaining <= 300 && !_warned5)
        {
            _warned5 = true;
            ShowWarningToast(5, critical: false);
            SoundAlert.Info();
        }
    }

    private void ShowWarningToast(int minutesLeft, bool critical)
    {
        _toast?.Close();

        var toast = new ToastWindow(minutesLeft, critical);
        toast.ExtendRequested += OnToastExtend;
        toast.Closed += (_, _) =>
        {
            if (ReferenceEquals(_toast, toast)) _toast = null;
        };

        _toast = toast;
        toast.Show();
    }

    /// <summary>Продление сеанса из напоминания: только по паролю администратора.</summary>
    private void OnToastExtend(object? sender, EventArgs e)
    {
        if (_toast != null) _toast.Topmost = false;   // не перекрывать диалог пароля

        // Topmost: диалог должен быть виден даже поверх игры в полном экране.
        var prompt = new LoginWindow(GateMode.AdminPrompt) { Owner = this, Topmost = true };
        if (prompt.ShowDialog() != true) return;

        App.ExtendSession(5);
        _warned5 = _warned1 = false;
        RefreshUi();
        ApplyPanelSettings();
        LoadPcInfo();

        var s = App.Settings;
        if (!s.Locked && !_tick.IsEnabled) _tick.Start();
    }

    private void ExpireSession()
    {
        if (App.Settings.Locked) return;

        _tick.Stop();
        _toast?.Close();
        _warned5 = _warned1 = false;

        try { App.Runner.KillCurrent(); } catch { /* игра уже завершена */ }

        // Закрываем Steam/Rockstar/Epic, чтобы игру нельзя было запустить из них.

        HistoryLog.Add("expire");

        App.Settings.Locked = true;
        App.Settings.RemainingSeconds = 0;
        App.Settings.PausedRemainingSeconds = 0;
        App.Settings.TimerPaused = false;
        App.Settings.SessionEndUtc = default;
        App.SaveState();
        RefreshUi();
        ApplyPanelSettings();
        LoadPcInfo();

        RequestUnlockOrExit();
    }

    /// <summary>Экран ограничения: пароль = новый сеанс игры, закрытие = выход.</summary>
    private void RequestUnlockOrExit()
    {
        if (App.ShowRestrictedGate(this))
        {
            RefreshUi();
        ApplyPanelSettings();
        LoadPcInfo();
            _tick.Start();

            // Клуб: киоск-режим и экран приветствия.
            try
            {
                KioskMode.Apply(App.Settings.KioskMode);
                if (App.Settings.KioskMode) Topmost = true;
            }
            catch { }

            ShowWelcome();
        }
        else
        {
            ShutdownApp();
        }
    }
    // ---------- Запуск игр ----------

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not GameViewModel vm) return;

        if (!EnsureCanPlay()) return;

        LaunchGame(vm.Item);
    }

    /// <summary>Запускает игру и показывает табличку «Игра запускается, ждите…».</summary>
    private void LaunchGame(GameItem game)
    {
        LaunchOverlayWindow? overlay = null;

        try
        {
            overlay = new LaunchOverlayWindow(game.Title, GameControls.For(game.Title));
            overlay.Show();

            App.Runner.Start(game);
        }
        catch (Exception ex)
        {
            overlay?.Close();
            MessageBox.Show(this,
                $"Не удалось запустить «{game.Title}».\n\n{ex.Message}",
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        UpdateGameStatus();
    }

    /// <summary>Фоном проверяет обновления и показывает окно, если есть новая версия.</summary>
    private async Task CheckUpdatesAsync()
    {
        var source = App.Settings.UpdateUrl;
        if (string.IsNullOrWhiteSpace(source)) return;

        try
        {
            var info = await UpdateService.CheckAsync(source, CancellationToken.None);
            if (info == null) return;

            await Dispatcher.InvokeAsync(() =>
            {
                var window = new UpdateWindow(info, source) { Owner = this };
                window.Show();
            });
        }
        catch { /* обновления не критичны */ }
    }

    // ---------- Друзья в локальной сети ----------

    private void RefreshFriends()
    {
        UserNameText.Text = "Профиль: " + LanPresence.SelfName;

        var peers = LanPresence.GetPeers();
        string signature = string.Join("|", peers.Select(p => p.Id + ":" + p.Name + ":" + p.Game))
                           + "#" + string.Join("|", _unread.Select(kv => kv.Key + "=" + kv.Value));

        if (signature == _friendsSignature) return;
        _friendsSignature = signature;

        // «Печатает…» живёт 3 секунды.
        foreach (var id in _typing.Where(kv => DateTime.UtcNow - kv.Value > TimeSpan.FromSeconds(3))
                                  .Select(kv => kv.Key).ToList())
            _typing.Remove(id);

        var items = peers.Select(p => new FriendVm
        {
            Peer = p,
            Name = p.Name,
            Status = p.Status,
            Typing = _typing.ContainsKey(p.Id),
            NameBrush = ThemeBrush(p.Theme),
            Unread = _unread.TryGetValue(p.Id, out int n) ? n : 0,
        }).ToList();

        FriendsList.ItemsSource = items;
        FriendsCount.Text = peers.Count == 0 ? "" : peers.Count.ToString();
        FriendsEmpty.Visibility = peers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Пришло сообщение от друга.</summary>
    private void OnChatMessage(LanPeer peer, string text)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            // Чат с этим другом уже открыт — показываем сообщение во вкладке, без уведомления.
            if (ChatWindow.IsOpenWith(peer.Id))
            {
                ChatWindow.NotifyIncoming(peer, text);
                return;
            }

            _unread[peer.Id] = _unread.TryGetValue(peer.Id, out int n) ? n + 1 : 1;
            SoundAlert.Info();
            RefreshFriends();

            new NoticeWindow("Новое сообщение — " + peer.Name, text, () => OpenChat(peer)).Show();
        }));
    }

    /// <summary>Цвет имени друга — его акцент из темы приложения.</summary>
    private static Brush ThemeBrush(string themeKey)
    {
        try
        {
            var theme = Themes.Find(themeKey);
            var color = (Color)ColorConverter.ConvertFromString(theme.Accent)!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return FindResourceStatic("TextBrush");
        }
    }

    private static Brush FindResourceStatic(string key)
        => Application.Current?.TryFindResource(key) as Brush ?? Brushes.White;

    private void OnTyping(LanPeer peer)
    {
        _typing[peer.Id] = DateTime.UtcNow;
        Dispatcher.BeginInvoke(new Action(RefreshFriends));
    }

    /// <summary>Переключает мой статус: в сети → отошёл → не беспокоить.</summary>
    private void Status_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = fe, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (key, title) in new[] { ("online", "В сети"), ("away", "Отошёл"), ("dnd", "Не беспокоить") })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = title,
                IsCheckable = true,
                IsChecked = string.Equals(App.Settings.MyStatus, key, StringComparison.OrdinalIgnoreCase),
            };
            var captured = key;
            item.Click += (_, _) =>
            {
                App.Settings.MyStatus = captured;
                App.SaveState();
                RefreshFriends();
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void OpenChat(LanPeer peer)
    {
        _unread.Remove(peer.Id);
        RefreshFriends();

        ChatWindow.Open(peer, this);
    }

    private void FriendChat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not FriendVm vm) return;

        OpenChat(vm.Peer);
    }

    private CallWindow? _callWindow;

    private void FriendCall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not FriendVm vm) return;
        var peer = vm.Peer;

        CallLauncher.Call(peer, this);
    }

    /// <summary>Включает голосовой движок и глобальный PTT.</summary>
    private bool StartCall(Guid room)
    {
        VoiceEngine.InputDeviceName = App.Settings.VoiceInputDevice;
        VoiceEngine.OutputDeviceName = App.Settings.VoiceOutputDevice;
        VoiceEngine.OpenMic = App.Settings.VoiceMode == "open";
        VoiceEngine.VoiceKey = App.Settings.VoiceKey;
        VoiceEngine.InputGain = App.Settings.VoiceInputGain;
        PttHook.Key = App.Settings.VoiceKey;

        if (!VoiceEngine.StartCall(room))
        {
            MessageBox.Show(this,
                "Не удалось включить микрофон или занять порт звонка.\n" +
                "Проверьте, что микрофон подключён.",
                "Звонок", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        PttHook.Install();
        return true;
    }

    private void ShowCallWindow()
    {
        if (_callWindow is { IsVisible: true })
        {
            _callWindow.Activate();
            return;
        }

        _callWindow = new CallWindow { Owner = this };
        _callWindow.Closed += (_, _) => _callWindow = null;
        _callWindow.Show();
    }

    /// <summary>Друг позвал в звонок: окно в стиле лаунчера, со звуком до ответа.</summary>
    private void OnCallInvited(Guid room, string fromName)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (VoiceEngine.InCall && VoiceEngine.RoomId != room) return;

            var prompt = new IncomingCallWindow(fromName)
            {
                Owner = IsVisible ? this : null,
                Topmost = true,
            };

            bool accepted = prompt.ShowDialog() == true && prompt.Accepted;
            if (!accepted) return;

            if (!VoiceEngine.InCall && !StartCall(room)) return;
            ShowCallWindow();
        }));
    }

    /// <summary>Тень у шапки и нижней панели, когда список прокручен — эффект «стекла».</summary>
    private void Library_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        bool scrolled = e.VerticalOffset > 4;

        var shadow = scrolled
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                BlurRadius = 20,
                ShadowDepth = 3,
                Opacity = 0.55,
            }
            : null;

        HeaderBar.Effect = shadow;
        BottomBar.Effect = scrolled
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Colors.Black,
                BlurRadius = 20,
                ShadowDepth = -3,
                Opacity = 0.55,
            }
            : null;

        // Лёгкий параллакс кинематографичного фона: он «плывёт» медленнее карточек.
        CinemaShift.Y = Math.Clamp(-e.VerticalOffset * 0.032, -36, 36);
    }

    // ---------- Экран приветствия и завершение сессии ----------

    private void ShowWelcome()
    {
        LoadQrCodes();
        LoadWelcomeAd();

        WelcomeOverlay.Visibility = Visibility.Visible;
        WelcomeOverlay.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            0, 1, TimeSpan.FromMilliseconds(220)));

        WelcomeShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
                },
            });
    }

    /// <summary>Обновляет блок рекламы на уже открытом экране приветствия (из админ-меню).</summary>
    public void RefreshWelcomeAd() => LoadWelcomeAd();

    /// <summary>Блок рекламы постов из соцсетей: текст задаётся в админ-меню.</summary>
    private void LoadWelcomeAd()
    {
        var s = App.Settings;

        WelcomeAdTitleText.Text = string.IsNullOrWhiteSpace(s.WelcomeAdTitle)
            ? "Реклама постов из соцсетей"
            : s.WelcomeAdTitle.Trim();

        WelcomeAdTextText.Text = s.WelcomeAdText?.Trim() ?? "";
        WelcomeAdTextText.Visibility = WelcomeAdTextText.Text.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        WelcomeAdCard.Visibility = s.WelcomeAdEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LoadQrCodes()
    {
        var dir = Path.Combine(ConfigStore.ConfigDirectory, "qr");

        // Свои картинки из папки qr, иначе встроенные QR на Телеграм и ВКонтакте.
        Qr1.Source = LoadQr(Path.Combine(dir, "qr1.png")) ?? LoadResourceImage("/Assets/qr-tg.png");
        Qr2.Source = LoadQr(Path.Combine(dir, "qr2.png")) ?? LoadResourceImage("/Assets/qr-vk.png");
        Qr3.Source = LoadQr(Path.Combine(dir, "qr3.png")) ?? LoadResourceImage("/Assets/qr-max.png");

        // Блоки без картинки не показываем, чтобы не висели пустые рамки.
        QrBlock1.Visibility = Qr1.Source == null ? Visibility.Collapsed : Visibility.Visible;
        QrBlock2.Visibility = Qr2.Source == null ? Visibility.Collapsed : Visibility.Visible;
        QrBlock3.Visibility = Qr3.Source == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static System.Windows.Media.Imaging.BitmapImage? LoadResourceImage(string packPath)
    {
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.UriSource = new Uri("pack://application:,,," + packPath, UriKind.Absolute);
            image.EndInit();
            return image;
        }
        catch { return null; }
    }

    private static System.Windows.Media.Imaging.BitmapImage? LoadQr(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            return image;
        }
        catch { return null; }
    }

    /// <summary>Тариф на экране приветствия: старт сессии заданной длины (по паролю админа).</summary>
    private void WelcomeTariff_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;
        if (!int.TryParse(tag, out int minutes)) return;

        if (!RequireAdmin()) return;

        var settings = App.Settings;
        settings.SessionMinutes = Math.Clamp(minutes, 30, 240);
        settings.TimerEnabled = true;
        settings.Locked = false;
        settings.TimerPaused = false;

        App.StartNewSession();
        App.SaveState();

        RefreshUi();
        UpdateGameStatus();
        WelcomeOverlay.Visibility = Visibility.Collapsed;

        HistoryLog.Add("time_set", detail: minutes + " мин (тариф на экране приветствия)");
    }

    private void WelcomeStart_Click(object sender, RoutedEventArgs e)
        => WelcomeOverlay.Visibility = Visibility.Collapsed;

    private void Help_Click(object sender, RoutedEventArgs e)
        => new HelpWindow { Owner = this }.ShowDialog();

    private void PromoCode_Click(object sender, RoutedEventArgs e)
    {
        new PromoRedeemWindow { Owner = this }.ShowDialog();
        RefreshUi();
    }

    private void Promo_Click(object sender, RoutedEventArgs e)
        => new PromoWindow { Owner = this }.ShowDialog();

    private void Support_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://t.me/qpokycnow") { UseShellExecute = true });
        }
        catch { /* нет браузера — не мешаем */ }
    }

    /// <summary>Админ завершает сессию и освобождает место следующему гостю.</summary>
    private void EndSession_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin()) return;

        var answer = MessageBox.Show(this,
            "Завершить сессию и вернуть экран приветствия?\n\nТекущая игра будет закрыта, таймер сброшен.",
            "Завершить сессию", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        try { App.Runner.KillCurrent(); } catch { }

        var s = App.Settings;
        s.TimerEnabled = false;
        s.TimerPaused = false;
        s.Locked = false;
        s.RemainingSeconds = 0;
        s.PausedRemainingSeconds = 0;
        s.SessionEndUtc = default;
        App.SaveState();

        RefreshTimerUi();
        UpdateGameStatus();
        ShowWelcome();
    }

    // ---------- Быстрые ссылки ----------

    private void OpenBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (!App.AdminUnlocked && !RequireAdmin()) return;

        try
        {
            Process.Start(new ProcessStartInfo("https://www.google.com") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось открыть браузер:\n\n" + ex.Message,
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSteam_Click(object sender, RoutedEventArgs e)
    {
        if (!App.AdminUnlocked && !RequireAdmin()) return;

        try
        {
            Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось открыть Steam:\n\n" + ex.Message,
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Друзья Steam: открываем список друзей в клиенте Steam.</summary>
    private void SteamFriends_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("steam://open/friends") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось открыть друзей Steam:\n\n" + ex.Message,
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>В библиотеку добавились игры из Steam (фоновый watcher) — перестраиваем список.</summary>
    private void OnLibraryGrown(int count)
    {
        if (!IsVisible) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                RebuildLibrary();
                RefreshUi();
            }
            catch { /* не мешаем игре */ }
        }));
    }

    // ---------- Карточка выбранной игры ----------

    private bool _detailsCollapsed;

    /// <summary>Сворачивает/разворачивает описание игры в одну строку.</summary>
    private bool _pcInfoCollapsed;

    /// <summary>Характеристики компьютера заполняются один раз при старте.</summary>
    private void LoadPcInfo()
    {
        PcCpuText.Text = "Процессор: " + SystemInfo.CpuName;
        PcRamText.Text = "Память: " + SystemInfo.RamText;
        PcGpuText.Text = "Видео: " + SystemInfo.GpuName;
        PcOsText.Text = SystemInfo.OsText + "  •  " + SystemInfo.ScreenText;
    }

    private void TogglePcInfo_Click(object sender, RoutedEventArgs e)
    {
        _pcInfoCollapsed = !_pcInfoCollapsed;

        PcInfoBody.Visibility = _pcInfoCollapsed ? Visibility.Collapsed : Visibility.Visible;
        PcInfoChevron.RenderTransform = new System.Windows.Media.RotateTransform(_pcInfoCollapsed ? -90 : 0);
        PcInfoChevron.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
    }

    /// <summary>ПКМ по блоку характеристик — скрыть его (вернуть можно в админ-меню или в Ctrl+K).</summary>
    private void HidePcSpecs_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.ShowPcSpecs = false;
        App.SaveState();
        ApplyPanelSettings();
    }

    private void ToggleDetails_Click(object sender, RoutedEventArgs e)
    {
        _detailsCollapsed = !_detailsCollapsed;

        DetailsBody.Visibility = _detailsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        DetailsChevron.RenderTransform = new System.Windows.Media.RotateTransform(_detailsCollapsed ? -90 : 0);
        DetailsChevron.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        DetailsHeaderText.Text = _detailsCollapsed ? "Описание (свернуто)" : "Описание";
    }


    /// <summary>Блик на обложке карточки следует за курсором — «дорогой» глянец.</summary>
    private void CardCover_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement cover || cover.DataContext is not GameViewModel vm) return;

        double width = cover.ActualWidth;
        double height = cover.ActualHeight;
        if (width <= 0 || height <= 0) return;

        var position = e.GetPosition(cover);
        vm.HighlightCenter = new Point(
            Math.Clamp(position.X / width, 0, 1),
            Math.Clamp(position.Y / height, 0, 1));
    }

    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GameViewModel vm)
            ShowDetails(vm);
    }

    private void ShowDetails(GameViewModel? vm)
    {
        _detailVm = vm;

        // Кинематографичный фон под выбранную игру (кроссфейд 250 мс).
        UpdateCinemaBackground(vm);

        if (vm == null)
        {
            DetailTitle.Text = "Выберите игру";
            DetailSubtitle.Text = "";
            DetailGenres.Text = "";
            DetailDescription.Text = "";
            DetailCover.Source = null;
            DetailCoverLetter.Text = "";
            DetailPlayButton.IsEnabled = false;
            return;
        }

        var game = vm.Item;

        DetailTitle.Text = vm.Title;
        DetailCover.Source = vm.CoverImage;
        DetailCoverLetter.Text = vm.CoverImage == null ? vm.Initial : "";

        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(game.ReleaseYear)) bits.Add(game.ReleaseYear!);
        if (!string.IsNullOrWhiteSpace(game.Developer)) bits.Add(game.Developer!);
        if (game.Metacritic.HasValue) bits.Add("Metacritic " + game.Metacritic);

        DetailSubtitle.Text = string.Join("   •   ", bits);
        DetailGenres.Text = game.Genres ?? "";
        // Описание показываем сокращённо, чтобы панель не приходилось прокручивать.
        var description = game.Description ?? "";
        if (description.Length > 420)
            description = description[..417].TrimEnd() + "…";

        DetailDescription.Text = string.IsNullOrWhiteSpace(description)
            ? "Метаданные не загружены. Ctrl+1 → выберите игру → «Метаданные»."
            : description;

        DetailPlayButton.IsEnabled = true;

        // Мягкое появление текста при смене игры.
        DetailsBody.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            0.65, 1, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
        });
    }

    private void DetailPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_detailVm == null) return;
        if (!EnsureCanPlay()) return;

        LaunchGame(_detailVm.Item);
    }

    private void ToggleGameLock(GameItem game)
    {
        if (!game.Locked && !App.AdminUnlocked && !RequireAdmin()) return;

        game.Locked = !game.Locked;
        App.SaveState();
        RebuildLibrary();
        UpdateGameStatus();
    }

    /// <summary>Фоном догружает метаданные для игр, у которых их ещё нет.</summary>
    private async Task FillMissingMetadataAsync()
    {
        var targets = App.Settings.Games
            .Where(g => string.IsNullOrWhiteSpace(g.Description))
            .Take(12)
            .ToList();

        if (targets.Count == 0) return;

        foreach (var game in targets)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var meta = await SteamMetadataService.FetchAsync(game.AppId, game.Title, cts.Token);
                if (meta != null) SteamMetadataService.Apply(game, meta);
            }
            catch { /* одну игру пропускаем, идём к следующей */ }

            await Task.Delay(1200);
        }

        App.SaveState();
    }

    /// <summary>Разрешает играть только при оставшемся времени.</summary>
    private bool EnsureCanPlay()
    {
        var s = App.Settings;

        // Таймер выключен — ограничений нет.
        if (!s.TimerEnabled) return true;

        if (!s.Locked && App.ComputeRemainingSeconds() > 0) return true;

        // Время истекло — доступ ограничен до ввода пароля.
        s.Locked = true;
        s.RemainingSeconds = 0;
        App.SaveState();


        if (App.ShowRestrictedGate(this))
        {
            RefreshUi();
        ApplyPanelSettings();
        LoadPcInfo();
            _tick.Start();

            // Клуб: киоск-режим и экран приветствия.
            try
            {
                KioskMode.Apply(App.Settings.KioskMode);
                if (App.Settings.KioskMode) Topmost = true;
            }
            catch { }

            ShowWelcome();
            return true;
        }

        ShutdownApp();
        return false;
    }

    // ---------- Обновление UI ----------

    private void RefreshUi()
    {
        RefreshTimerUi();
        UpdateGameStatus();
        UpdateAchievementsButton();
    }

    /// <summary>Счётчик на кнопке: сколько достижений уже открыто.</summary>
    private void UpdateAchievementsButton()
    {
        try
        {
            int unlocked = Achievements.UnlockedCount;
            string text = unlocked > 0
                ? "\U0001F3C6  " + unlocked + " / " + Achievements.TotalCount
                : "Достижения";

            if (!Equals(AchievementsButton.Content, text)) AchievementsButton.Content = text;
        }
        catch { /* кнопка ещё не создана — не критично */ }
    }

    private void Achievements_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Окно само догонит заслуженное — без лишних тостов.
            if (_achievementsWindow != null)
            {
                _achievementsWindow.Activate();
                return;
            }

            var window = new AchievementsWindow();
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_achievementsWindow, window)) _achievementsWindow = null;
            };

            _achievementsWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось открыть достижения:\n\n" + ex.Message,
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>Новое достижение: показываем тост, игру это не сворачивает.</summary>
    private void OnAchievementsUnlocked(List<Achievement> fresh)
    {
        if (fresh == null || fresh.Count == 0) return;
        if (App.SmokeMode) return;   // в самопроверке окна не мешаем

        try
        {
            if (fresh.Count == 1)
            {
                var one = fresh[0];
                new AchievementToast(one.Icon, one.Title, one.Description).Show();
            }
            else
            {
                var names = string.Join(", ", fresh.Take(3).Select(a => a.Title));
                if (fresh.Count > 3) names += " и ещё " + (fresh.Count - 3);

                new AchievementToast("\U0001F3C6",
                    "Открыто достижений: " + fresh.Count, names).Show();
            }
        }
        catch { /* тост — украшение, не повод падать */ }
    }

    private void RefreshTimerUi()
    {
        var s = App.Settings;

        // Таймер выключен — прячем весь блок времени.
        if (!s.TimerEnabled)
        {
            TimerChip.Visibility = Visibility.Collapsed;
            return;
        }

        TimerChip.Visibility = Visibility.Visible;

        int total = Math.Max(1, s.SessionMinutes * 60);

        // Остаток всегда считаем от дедлайна, а не от последнего сохранённого счётчика.
        int remaining = App.ComputeRemainingSeconds();
        s.RemainingSeconds = remaining;

        string formatted = FormatTime(remaining);

        if (remaining > 0)
        {
            TimerChipText.Text = s.TimerPaused ? formatted + " ⏸" : formatted;

            // Меняем цвет по мере приближения конца: спокойно → оранжевый → красный.
            TimerChipText.Foreground = s.TimerPaused
                ? FindResource("TextSecondaryBrush") as Brush ?? Brushes.White
                : remaining <= 60
                    ? FindResource("DangerBrush") as Brush ?? Brushes.OrangeRed
                    : remaining <= 300
                        ? WarnBrush
                        : FindResource("TextBrush") as Brush ?? Brushes.White;

            SessionStatusText.Text = s.TimerPaused
                ? "Пауза — таймер остановлен (" + formatted + ")"
                : "Осталось: " + formatted;
            SessionTotalText.Text = $"из {s.SessionMinutes} мин";
            TimeProgress.Value = 100.0 * remaining / total;
            SessionStatusText.Foreground = s.TimerPaused
                ? FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray
                : remaining <= 60
                    ? FindResource("DangerBrush") as Brush ?? Brushes.OrangeRed
                    : FindResource("TextBrush") as Brush ?? Brushes.White;
        }
        else
        {
            TimerChipText.Text = "--:--";
            SessionStatusText.Text = "Время истекло — введите пароль";
            SessionTotalText.Text = "";
            TimeProgress.Value = 0;
            SessionStatusText.Foreground = FindResource("DangerBrush") as Brush ?? Brushes.White;
        }

    }

    /// <summary>Применяет настройки прозрачности панелей.</summary>
    private void ApplyPanelSettings()
    {
        bool transparent = App.Settings.TransparentPanels;

        FriendsPanel.Opacity = transparent ? 0.72 : 1.0;
        DetailsPanel.Opacity = transparent ? 0.72 : 1.0;

        // Характеристики ПК можно выключить в админ-меню.
        PcInfoBlock.Visibility = App.Settings.ShowPcSpecs ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UpdateGameStatus()
    {
        bool running = App.Runner.IsRunning;
        string? title = App.Runner.CurrentTitle;

        if (running && !string.IsNullOrEmpty(title))
        {
            GameStatusDot.Fill = FindResource("OkBrush") as Brush ?? Brushes.LimeGreen;
            GameStatusText.Text = "Игра идёт: " + title;
            GameStatusText.Foreground = FindResource("TextBrush") as Brush ?? Brushes.White;
        }
        else
        {
            GameStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x7A));
            GameStatusText.Text = "Игра не запущена";
            GameStatusText.Foreground = FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
        }

        foreach (var vm in App.Library.Items)
            vm.IsRunning = running && string.Equals(vm.Title, title, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTime(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";
    }

    // ---------- Клавиатура и кнопки окна ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (!ctrl) return;

        if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OpenCommandPalette();
            return;
        }

        if (e.Key is Key.D1 or Key.NumPad1)
        {
            e.Handled = true;
            OpenAdminMenu();
        }
        else if (e.Key is Key.D2 or Key.NumPad2)
        {
            e.Handled = true;
            ToggleLock();
        }
    }

    /// <summary>Клик по логотипу «Top Racer Launcher» открывает админ-меню (как Ctrl+1).</summary>
    private void Logo_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        e.Handled = true;
        OpenAdminMenu();
    }

    /// <summary>Командная палитра: игры и быстрые действия (Ctrl+K).</summary>
    private void OpenCommandPalette()
    {
        var items = new List<PaletteItem>();

        foreach (var game in App.Settings.Games)
        {
            if (game.Locked && !App.AdminUnlocked) continue;

            var target = game;
            items.Add(new PaletteItem
            {
                Title = target.Title,
                Subtitle = "Играть",
                Run = () =>
                {
                    if (!EnsureCanPlay()) return;
                    LaunchGame(target);
                },
            });
        }

        items.Add(new PaletteItem { Title = "Админ-меню", Subtitle = "Ctrl+1", Run = OpenAdminMenu });
        items.Add(new PaletteItem { Title = "Завершить сессию", Subtitle = "Клуб", Run = () => EndSession_Click(this, new RoutedEventArgs()) });
        items.Add(new PaletteItem { Title = "Экран приветствия", Subtitle = "Показать", Run = ShowWelcome });
        items.Add(new PaletteItem { Title = "Открыть Steam", Subtitle = "Клиент", Run = () => OpenSteam_Click(this, new RoutedEventArgs()) });
        items.Add(new PaletteItem { Title = "Открыть браузер", Subtitle = "Интернет", Run = () => OpenBrowser_Click(this, new RoutedEventArgs()) });
        items.Add(new PaletteItem
        {
            Title = App.Settings.ShowPcSpecs ? "Скрыть характеристики ПК" : "Показать характеристики ПК",
            Subtitle = "Главный экран",
            Run = () =>
            {
                App.Settings.ShowPcSpecs = !App.Settings.ShowPcSpecs;
                App.SaveState();
                ApplyPanelSettings();
            },
        });
        items.Add(new PaletteItem { Title = "Проверить обновления", Subtitle = "Версия " + UpdateService.CurrentVersion, Run = () => _ = CheckUpdatesAsync() });
        items.Add(new PaletteItem { Title = "Что нового", Subtitle = "История версий", Run = () => ChangelogWindow.Open(this) });

        new CommandPalette(items) { Owner = this }.ShowDialog();
    }

    private void OpenAdminMenu()
    {
        if (!RequireAdmin()) return;

        _adminOpen = true;
        _tick.Stop();   // под модалкой не открываем экран блокировки и не дёргаем UI
        try
        {
            var admin = new AdminWindow { Owner = this };
            admin.ShowDialog();
        }
        finally
        {
            _adminOpen = false;
        }

        // В админ-меню могли добавить игры, изменить время, тему, пропорции или фон.
        ApplyCardSettings();
        ApplyPanelSettings();
        LoadPcInfo();
        WindowState = App.Settings.FullScreen ? WindowState.Maximized : WindowState.Normal;
        RebuildLibrary();
        LoadAppBackground();
        RefreshAfisha();
        RefreshUi();
        ApplyPanelSettings();
        LoadPcInfo();

        if (App.Settings.Locked)
        {
            // Время сбросили/истекло, пока было открыто меню.
            RequestUnlockOrExit();
            return;
        }

        if (App.Settings.TimerEnabled && App.ComputeRemainingSeconds() <= 0 && App.Settings.SessionEverStarted)
        {
            ExpireSession();
            return;
        }

        if (!_tick.IsEnabled) _tick.Start();
    }

    private void ToggleMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        // Пока замок закрыт — свернуть нельзя без пароля администратора.
        if (!App.AdminUnlocked && !RequireAdmin()) return;

        _allowMinimize = true;
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// Свернуть в обход кнопки (Win+D, «Показать рабочий стол») нельзя:
    /// окно сразу восстанавливается и просит пароль.
    /// </summary>
    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && !_allowMinimize)
        {
            // Замок открыт — сворачивание разрешено, ничего не делаем.
            if (App.AdminUnlocked) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                WindowState = WindowState.Maximized;
                Activate();
                RequireAdmin();
            }));
            return;
        }

        if (WindowState != WindowState.Minimized)
            _allowMinimize = false;
    }

    // ---------- Размер карточек (ползунок) ----------

    /// <summary>Применяет размер и пропорции карточек из настроек.</summary>
    public void ApplyCardSettings()
    {
        double savedWidth = App.Settings.CardWidth;
        if (savedWidth < 100 || savedWidth > 380)
            savedWidth = CardSizes.Find(App.Settings.CardSize).Width;

        Ui.CardWidth = savedWidth;
        Ui.CardAspect = CardAspects.Find(App.Settings.CardAspect).Ratio;
        IconSizeSlider.Value = Ui.CardWidth;
        UpdateIconSizeLabel();
    }

    private void IconSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Ui == null || IconSizeLabel == null) return;

        Ui.CardWidth = e.NewValue;
        UpdateIconSizeLabel();
    }

    /// <summary>Сохранение размера с задержкой: не пишем конфиг на каждое нажатие стрелки.</summary>
    private void IconSizeSlider_Changed(object sender, EventArgs e)
    {
        _cardWidthSave.Stop();
        _cardWidthSave.Start();
    }

    private void UpdateIconSizeLabel() => IconSizeLabel.Text = ((int)Ui.CardWidth).ToString();

    // ---------- Фон приложения ----------

    /// <summary>Загружает фон приложения из настроек (если он задан).</summary>
    private void LoadAppBackground()
    {
        var path = App.Settings.AppBackgroundPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            AppBackgroundImage.Source = null;
            AppBackgroundImage.Visibility = Visibility.Collapsed;
            AppBackgroundOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            var bmp = ImageCache.Load(path, 1920);
            if (bmp == null) throw new InvalidOperationException("картинка не читается");

            AppBackgroundImage.Source = bmp;
            AppBackgroundImage.Visibility = Visibility.Visible;
            AppBackgroundOverlay.Visibility = Visibility.Visible;
        }
        catch
        {
            AppBackgroundImage.Source = null;
            AppBackgroundImage.Visibility = Visibility.Collapsed;
            AppBackgroundOverlay.Visibility = Visibility.Collapsed;
        }
    }

    // ---------- Кинематографичный фон выбранной игры ----------

    /// <summary>Какая из двух картинок сейчас на переднем плане (для кроссфейда).</summary>
    private bool _cinemaAIsFront = true;

    /// <summary>Ключ текущего фона — чтобы не перерисовывать ту же игру.</summary>
    private string? _cinemaKey;

    /// <summary>
    /// Ставит размытый фон выбранной игры: широкий фон, а если его нет — обложку.
    /// Переключение кроссфейдом 250 мс между двумя слоями, слой плавно появляется.
    /// </summary>
    private void UpdateCinemaBackground(GameViewModel? vm)
    {
        var path = vm == null ? null : FirstExisting(vm.Item.BackgroundPath, vm.Item.CoverPath);

        if (string.Equals(path, _cinemaKey, StringComparison.OrdinalIgnoreCase)) return;
        _cinemaKey = path;

        ShowCinemaImage(ImageCache.Load(path, 1100));
    }

    private static string? FirstExisting(params string?[] paths)
    {
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) return path;
        }

        return null;
    }

    private void ShowCinemaImage(ImageSource? image)
    {
        if (image == null)
        {
            FadeCinemaLayer(0, 460);
            return;
        }

        var incoming = _cinemaAIsFront ? CinemaImageB : CinemaImageA;
        var outgoing = _cinemaAIsFront ? CinemaImageA : CinemaImageB;

        // База: входящая картинка остаётся видимой, уходящая гаснет. Анимация — поверх.
        incoming.Opacity = 1;
        outgoing.Opacity = 0;

        if (!ReferenceEquals(incoming.Source, image))
            incoming.Source = image;

        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };

        incoming.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            0, 1, TimeSpan.FromMilliseconds(430))
        {
            EasingFunction = ease,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
        });

        outgoing.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            1, 0, TimeSpan.FromMilliseconds(430))
        {
            EasingFunction = ease,
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
        });

        _cinemaAIsFront = !_cinemaAIsFront;
        FadeCinemaLayer(1, 560);
    }

    /// <summary>Плавно включает/выключает весь слой (когда выбрана/снята игра).</summary>
    private void FadeCinemaLayer(double to, int milliseconds)
    {
        if (Math.Abs(CinemaLayer.Opacity - to) < 0.001) return;

        CinemaLayer.Opacity = to;
        CinemaLayer.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
            to > 0 ? 0 : 1, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
            },
            FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
        });
    }

    // ---------- Афиша клуба и анонс турнира ----------

    /// <summary>Перестраивает полосу афиши и тикер из настроек (после админ-меню и при старте).</summary>
    public void RefreshAfisha()
    {
        var settings = App.Settings;

        var cards = new List<AfishaCardVm>();
        if (settings.AfishaEnabled)
        {
            foreach (var item in settings.Afisha.Where(i => i.Enabled))
            {
                cards.Add(new AfishaCardVm
                {
                    Title = string.IsNullOrWhiteSpace(item.Title) ? "Событие" : item.Title.Trim(),
                    Text = item.Text?.Trim() ?? "",
                    DateText = item.DateText?.Trim() ?? "",
                    Image = string.IsNullOrWhiteSpace(item.ImagePath) ? null : ImageCache.Load(item.ImagePath, 640),
                });
            }
        }

        AfishaItems.ItemsSource = cards;
        AfishaStrip.Visibility = cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var ticker = (settings.AfishaTickerText ?? "").Trim();
        bool showTicker = settings.AfishaTickerEnabled && ticker.Length > 0;

        TickerBar.Visibility = showTicker ? Visibility.Visible : Visibility.Collapsed;
        TickerText.Text = ticker;
        TickerBar.ToolTip = showTicker ? ticker : null;

        // Длинный анонс не влезает в трей — включаем медленную бегущую строку.
        Dispatcher.BeginInvoke(new Action(StartTickerMarquee),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Бегущая строка анонса: если текст не влезает — плавно прокручиваем его.</summary>
    private void StartTickerMarquee()
    {
        TickerShift.BeginAnimation(TranslateTransform.XProperty, null);
        TickerShift.X = 0;

        if (TickerBar.Visibility != Visibility.Visible || TickerText.Text.Length == 0) return;

        double viewport = TickerViewport.ActualWidth;
        if (viewport <= 1) return;

        TickerText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double textWidth = TickerText.DesiredSize.Width;
        if (textWidth <= viewport) return;

        // Скорость ~34 px/с: медленно и читаемо, с паузой в начале и в конце.
        double distance = textWidth - viewport + 12;
        double scrollSeconds = distance / 34.0;

        var animation = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames
        {
            RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
        };

        animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(
            0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(
            0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5))));
        animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(
            -distance, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.5 + scrollSeconds))));
        animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(
            -distance, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4 + scrollSeconds))));

        TickerShift.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    // ---------- Контекстное меню игры (правая кнопка) ----------

    private void Card_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not GameViewModel vm) return;
        e.Handled = true;

        var menu = new ContextMenu { PlacementTarget = fe };

        var play = new MenuItem { Header = "Играть" };
        play.Click += (_, _) =>
        {
            if (EnsureCanPlay())
            {
                try { App.Runner.Start(vm.Item); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, $"Не удалось запустить «{vm.Title}».\n\n{ex.Message}",
                        "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                UpdateGameStatus();
            }
        };
        menu.Items.Add(play);
        menu.Items.Add(new Separator());

        var setup = new MenuItem { Header = "Настроить игру…" };
        setup.Click += (_, _) => EditGame(vm.Item);
        menu.Items.Add(setup);

        var cover = new MenuItem { Header = "Обложка…" };
        cover.Click += (_, _) => EditGameImage(vm.Item, ImageKind.Cover);
        menu.Items.Add(cover);

        var background = new MenuItem { Header = "Задний фон…" };
        background.Click += (_, _) => EditGameImage(vm.Item, ImageKind.Background);
        menu.Items.Add(background);

        menu.Items.Add(new Separator());

        var lockItem = new MenuItem { Header = vm.Item.Locked ? "Показать игру" : "Скрыть игру" };
        lockItem.Click += (_, _) => ToggleGameLock(vm.Item);
        menu.Items.Add(lockItem);

        menu.Items.Add(new Separator());

        var remove = new MenuItem { Header = "Удалить из библиотеки" };
        remove.Click += (_, _) => RemoveGame(vm.Item);
        menu.Items.Add(remove);

        menu.IsOpen = true;
    }

    // ---------- Замок (Ctrl+2) ----------

    private void LockToggle_Click(object sender, MouseButtonEventArgs e) => ToggleLock();

    /// <summary>Ctrl+2 / клик по замку: заблокировать или разблокировать окно.</summary>
    private void ToggleLock()
    {
        if (App.AdminUnlocked)
        {
            // Запереть можно сразу, без пароля.
            App.AdminUnlocked = false;
        }
        else
        {
            // Отпереть — только по паролю администратора.
            var prompt = new LoginWindow(GateMode.AdminPrompt) { Owner = this, Topmost = true };
            if (prompt.ShowDialog() != true) return;
            App.AdminUnlocked = true;
        }

        UpdateLockIndicator();
        RebuildLibrary();
    }

    /// <summary>Зелёный открытый замок = разблокировано, красный закрытый = заблокировано.</summary>
    private void UpdateLockIndicator()
    {
        bool unlocked = App.AdminUnlocked;

        LockBadge.Background = unlocked
            ? new SolidColorBrush(Color.FromRgb(0x0F, 0xB8, 0x5E))
            : new SolidColorBrush(Color.FromRgb(0xD6, 0x45, 0x45));
        LockText.Text = unlocked ? "Открыто" : "Закрыто";
        LockBadge.ToolTip = unlocked
            ? "Ctrl+2 — заблокировать окно (сворачивание и настройки потребуют пароль)"
            : "Ctrl+2 — разблокировать (потребуется пароль)";

        // Ползунок размера карточек — только для администратора.
        IconSizePanel.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Запрос пароля администратора. Второй раз за запуск не спрашиваем.</summary>
    private bool RequireAdmin()
    {
        if (App.AdminUnlocked) return true;

        var prompt = new LoginWindow(GateMode.AdminPrompt) { Owner = this };
        if (prompt.ShowDialog() != true) return false;

        App.AdminUnlocked = true;
        return true;
    }

    private void EditGame(GameItem game)
    {
        if (!RequireAdmin()) return;

        var dlg = new AddGameWindow(game) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        App.SaveState();
        RebuildLibrary();
        UpdateGameStatus();
    }

    private void EditGameImage(GameItem game, ImageKind kind)
    {
        if (!RequireAdmin()) return;

        var dlg = new CoverSearchWindow(kind, game.Title) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var oldPath = kind == ImageKind.Cover ? game.CoverPath : game.BackgroundPath;
        var newPath = dlg.ResultPath;

        // Не удаляем файл, если пользователь выбрал тот же самый: иначе картинка «сломается».
        if (!string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            ImageSearchService.DeleteIfMedia(oldPath);

        if (kind == ImageKind.Cover)
            game.CoverPath = string.IsNullOrEmpty(newPath) ? null : newPath;
        else
            game.BackgroundPath = string.IsNullOrEmpty(newPath) ? null : newPath;

        App.SaveState();
        RebuildLibrary();
        UpdateGameStatus();
    }

    private void RemoveGame(GameItem game)
    {
        if (!RequireAdmin()) return;

        var answer = MessageBox.Show(this,
            $"Удалить «{game.Title}» из библиотеки?",
            "TopRacerLauncher", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        SteamLibraryWatcher.Forget(game);
        App.Settings.Games.Remove(game);

        // Не оставляем «сиротские» картинки в media.
        ImageSearchService.DeleteIfMedia(game.CoverPath);
        ImageSearchService.DeleteIfMedia(game.BackgroundPath);

        App.SaveState();
        RebuildLibrary();
        UpdateGameStatus();
    }

    private void CloseApp_Click(object sender, RoutedEventArgs e)
    {
        // Разблокировано администратором — закрываем сразу, без пароля.
        if (App.AdminUnlocked)
        {
            ShutdownApp();
            return;
        }

        // Иначе пароль спросит OnClosing.
        Close();
    }

    private void ShutdownApp()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _friendsTimer.Stop();
        try { KioskMode.Disable(); } catch { }
        try { PttHook.Uninstall(); } catch { }
        try { VoiceEngine.LeaveCall(); } catch { }
        try { LanPresence.Stop(); } catch { }
        _forceClose = true;
        _tick.Stop();
        App.SaveState();
        App.RequestShutdown();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_forceClose || App.IsShuttingDown)
        {
            _tick.Stop();
            App.SaveState();
            return;
        }

        // Замок открыт администратором — закрываемся без пароля.
        if (App.AdminUnlocked)
        {
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(ShutdownApp));
            return;
        }

        // Закрытие разрешено только администратору: запрашиваем пароль.
        e.Cancel = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var prompt = new LoginWindow(GateMode.AdminPrompt) { Owner = this, Topmost = Topmost };
            if (prompt.ShowDialog() == true)
            {
                // Именно Shutdown: ShutdownMode = OnExplicitShutdown, иначе процесс
                // остаётся висеть без окон после Close().
                ShutdownApp();
            }
        }));
    }
}

/// <summary>Строка друга в панели «Друзья в сети».</summary>
public sealed class FriendVm
{
    public LanPeer Peer { get; init; } = new();
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public Brush NameBrush { get; init; } = Brushes.White;
    public bool Typing { get; init; }
    public int Unread { get; init; }
    public bool ShowUnread => Unread > 0;
    public string StatusText => Typing ? "печатает…" : Status;
}

/// <summary>Состояние UI главного окна (размер и пропорции карточек).</summary>
public sealed class UiState : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private double _cardWidth = 162;
    private double _cardAspect = 4.0 / 3.0;

    /// <summary>Ширина карточки в пикселях (значение ползунка 100–380).</summary>
    public double CardWidth
    {
        get => _cardWidth;
        set
        {
            _cardWidth = Math.Clamp(value, 100, 380);
            OnPropertyChanged(nameof(CardWidth));
            OnPropertyChanged(nameof(CardHeight));
            OnPropertyChanged(nameof(CoverFontSize));
        }
    }

    /// <summary>Пропорции карточки: высота / ширина (3:4 → 1.33, 1:1 → 1, 16:9 → 0.56).</summary>
    public double CardAspect
    {
        get => _cardAspect;
        set
        {
            _cardAspect = Math.Clamp(value, 0.4, 1.8);
            OnPropertyChanged(nameof(CardAspect));
            OnPropertyChanged(nameof(CardHeight));
        }
    }

    /// <summary>Высота обложки по выбранным пропорциям.</summary>
    public double CardHeight => Math.Round(_cardWidth * _cardAspect);

    /// <summary>Размер буквы-заглушки (пропорция исходного шрифта 72 при ширине 178).</summary>
    public double CoverFontSize => Math.Round(_cardWidth * 72.0 / 178.0);

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
}
