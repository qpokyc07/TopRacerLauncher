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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = Ui;

        // Размер карточек из настроек (миграция со старого формата S/M/L/XL).
        ApplyCardSettings();

        _tick.Tick += OnTick;
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

            // Поиск друзей в локальной сети.
            try { LanPresence.Start(); } catch { }
            LanPresence.CallInvited += OnCallInvited;
            LanPresence.MessageReceived += OnChatMessage;
            RefreshFriends();
            _friendsTimer.Start();
        };
        Activated += (_, _) => { if (!_adminOpen) RefreshUi(); };
        StateChanged += OnWindowStateChanged;
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;

        RefreshUi();
    }

    // ---------- Библиотека ----------

    private void RebuildLibrary()
    {
        App.Library.Items.Clear();
        foreach (var game in App.Settings.Games)
        {
            // Скрытые игры видны только в админ-режиме (зелёный замок).
            if (game.Locked && !App.AdminUnlocked) continue;
            App.Library.Items.Add(new GameViewModel(game));
        }

        GamesItems.ItemsSource = App.Library.Items;
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

            ImageSearchService.CleanupOrphans(keep);
        }
        catch { /* уборка не критична */ }
    }

    // ---------- Таймер ----------

    private void OnTick(object? sender, EventArgs e)
    {
        // Игра закрылась сама — фиксируем длительность в статистике.
        try { if (!App.Runner.IsRunning) App.Runner.CompleteSession(); } catch { }

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

        RequestUnlockOrExit();
    }

    /// <summary>Экран ограничения: пароль = новый сеанс игры, закрытие = выход.</summary>
    private void RequestUnlockOrExit()
    {
        if (App.ShowRestrictedGate(this))
        {
            RefreshUi();
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
            overlay = new LaunchOverlayWindow(game.Title);
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

        var items = peers.Select(p => new FriendVm
        {
            Peer = p,
            Name = p.Name,
            Status = p.Status,
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

        if (!VoiceEngine.InCall && !StartCall(Guid.NewGuid())) return;

        LanPresence.SendCallInvite(peer, VoiceEngine.RoomId);
        ShowCallWindow();
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

    /// <summary>Друг позвал в звонок.</summary>
    private void OnCallInvited(Guid room, string fromName)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (VoiceEngine.InCall && VoiceEngine.RoomId != room) return;

            // Звонок: рингтон, пока не ответили (до ~15 секунд).
            int rings = 0;
            var ring = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            ring.Tick += (_, _) =>
            {
                if (++rings > 10) { ring.Stop(); return; }
                SoundAlert.Warn();
            };
            ring.Start();

            MessageBoxResult answer;
            try
            {
                answer = MessageBox.Show(this,
                    $"«{fromName}» приглашает в голосовой звонок.\n\nПрисоединиться?",
                    "Звонок", MessageBoxButton.YesNo, MessageBoxImage.Question);
            }
            finally
            {
                ring.Stop();
            }

            if (answer != MessageBoxResult.Yes) return;

            if (!VoiceEngine.InCall && !StartCall(room)) return;
            ShowCallWindow();
        }));
    }

    // ---------- Экран приветствия и завершение сессии ----------

    private void ShowWelcome()
    {
        LoadQrCodes();
        WelcomeOverlay.Visibility = Visibility.Visible;
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

    private void WelcomeStart_Click(object sender, RoutedEventArgs e)
        => WelcomeOverlay.Visibility = Visibility.Collapsed;

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

    // ---------- Карточка выбранной игры ----------

    private bool _detailsCollapsed;

    /// <summary>Сворачивает/разворачивает панель описания (в половину ширины).</summary>
    private void ToggleDetails_Click(object sender, RoutedEventArgs e)
    {
        _detailsCollapsed = !_detailsCollapsed;

        DetailsPanel.Width = _detailsCollapsed ? 200 : 360;
        DetailCoverBlock.Visibility = _detailsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        DetailDescription.Visibility = _detailsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        DetailGenres.Visibility = _detailsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        DetailSubtitle.Visibility = _detailsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        DetailTitle.FontSize = _detailsCollapsed ? 14 : 19;

        DetailsToggle.Content = _detailsCollapsed ? "\u2039" : "\u203A";
        DetailsToggle.ToolTip = _detailsCollapsed ? "Развернуть описание" : "Свернуть описание";
    }


    private void Card_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is GameViewModel vm)
            ShowDetails(vm);
    }

    private void ShowDetails(GameViewModel? vm)
    {
        _detailVm = vm;

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
        DetailDescription.Text = string.IsNullOrWhiteSpace(game.Description)
            ? "Метаданные не загружены. Ctrl+1 → выберите игру → «Метаданные»."
            : game.Description!;

        DetailPlayButton.IsEnabled = true;
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

    private void UpdateGameStatus()
    {
        bool running = App.Runner.IsRunning;
        string? title = App.Runner.CurrentTitle;

        if (running && !string.IsNullOrEmpty(title))
        {
            GameStatusDot.Fill = FindResource("OkBrush") as Brush ?? Brushes.LimeGreen;
            GameStatusText.Text = "Игра идёт: " + title;
            GameStatusText.Foreground = FindResource("TextBrush") as Brush ?? Brushes.White;
            GameFooterText.Text = "Запущена игра: " + title;
            GameFooterSubText.Text = App.Settings.TimerEnabled
                ? "Закроется автоматически по таймеру"
                : "Ограничение по времени выключено";
        }
        else
        {
            GameStatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x6E, 0x6E, 0x7A));
            GameStatusText.Text = "Игра не запущена";
            GameStatusText.Foreground = FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
            GameFooterText.Text = "Игра не запущена";
            GameFooterSubText.Text = !App.Settings.TimerEnabled
                ? "Ограничение по времени выключено"
                : App.Settings.RemainingSeconds > 0
                    ? "Выберите игру в библиотеке"
                    : "Нет выданного времени — Ctrl+1";
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
        WindowState = App.Settings.FullScreen ? WindowState.Maximized : WindowState.Normal;
        RebuildLibrary();
        LoadAppBackground();
        RefreshUi();

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
        // Закрыть лаунчер может только администратор — пароль спросит OnClosing.
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

        // Закрытие разрешено только администратору: запрашиваем пароль.
        e.Cancel = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var prompt = new LoginWindow(GateMode.AdminPrompt) { Owner = this };
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
    public int Unread { get; init; }
    public bool ShowUnread => Unread > 0;
}

/// <summary>Состояние UI главного окна (размер и пропорции карточек).</summary>
public sealed class UiState : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    private double _cardWidth = 178;
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
