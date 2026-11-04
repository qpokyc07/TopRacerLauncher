using System.IO;
using System.Threading;
using System.Windows;

namespace TopRacerLauncher;

public partial class App : Application
{
    /// <summary>Файл журнала сбоев (диагностика).</summary>
    public static string CrashLogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TopRacerLauncher", "crash.log");

    /// <summary>Настройки, загруженные при старте.</summary>
    public static Settings Settings { get; private set; } = new();

    /// <summary>Библиотека игр для UI.</summary>
    public static Library Library { get; } = new();

    /// <summary>Админ уже вводил пароль в этом запуске — не спрашиваем повторно.</summary>
    public static bool AdminUnlocked { get; set; }

    /// <summary>Режим самопроверки (--smoke-test): окна создаются, тосты не показываем.</summary>
    public static bool SmokeMode { get; private set; }

    /// <summary>Управление запущенной игрой.</summary>
    public static GameRunner Runner { get; } = new();

    private static Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Журнал сбоев: любое необработанное исключение пишется в файл для диагностики.
        DispatcherUnhandledException += (_, ex) =>
        {
            LogCrash("[UI]", ex.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            LogCrash("[FATAL]", ex.ExceptionObject);
        };

        bool smoke = HasArg(e.Args, "--smoke-test");
        SmokeMode = smoke;

        // Демо-режимы (только Debug) показывают одно окно и не должны конфликтовать
        // с уже запущенным лаунчером на этом ПК.
        bool demoMode = e.Args.Any(a => a.StartsWith("--demo-", StringComparison.OrdinalIgnoreCase));
#if DEBUG
        // Отладочные скриншоты не должны упираться в проверку одного экземпляра.
        demoMode |= e.Args.Any(a => a.StartsWith("--shot-", StringComparison.OrdinalIgnoreCase));
#endif

        // Один экземпляр: два лаунчера = два таймера и гонка записи конфига.
        if (!smoke && !demoMode)
        {
            _instanceMutex = new Mutex(true, @"Local\TopRacerLauncher.SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                // Важно: до чтения конфига выходить нельзя сохранять состояние —
                // иначе пустые настройки затрут пароль и библиотеку.
                MessageBox.Show(
                    "TopRacerLauncher уже запущен.\n\nНайдите окно лаунчера на панели задач.",
                    "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }

        if (smoke)
        {
            // Самопроверка: работаем во временном каталоге, ничего не трогая.
            var tmp = Path.Combine(Path.GetTempPath(), "TopRacerLauncherSmoke");
            try { Directory.Delete(tmp, recursive: true); } catch { /* не страшно */ }
            ConfigStore.UseDirectory(tmp);
        }

        Settings = ConfigStore.Load();
        _settingsLoaded = true;

        try { PlayStats.Trim(); } catch { /* статистика не критична */ }

        // Хвосты от автообновления (старый exe и недокопированный новый).
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                foreach (var suffix in new[] { ".old", ".new" })
                {
                    var leftover = exe + suffix;
                    if (File.Exists(leftover)) File.Delete(leftover);
                }
            }
        }
        catch { /* занято — удалим в следующий раз */ }

        if (ConfigStore.FatalError != null)
        {
            // Молча начинать с чистого конфига нельзя: это сброс пароля и потеря библиотеки.
            MessageBox.Show(
                "Не удалось прочитать файл настроек:\n" + ConfigStore.ConfigPath +
                "\n\nОшибка: " + ConfigStore.FatalError +
                "\n\nИспорченный файл сохранён рядом с копией .corrupt. " +
                "Восстановите config.json из копии .bak или удалите файл, чтобы начать заново " +
                "(пароль и библиотека при этом будут потеряны).",
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        if (ConfigStore.Warning != null)
        {
            HistoryLog.Add("config_recovered", detail: ConfigStore.Warning);
            MessageBox.Show(ConfigStore.Warning + "\n\nПродолжаем с восстановленными настройками.",
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Статистика запусков (общая для всех учётных записей на этом ПК).
        if (!smoke && !demoMode) UsageStats.RecordLaunch();

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Применяем сохранённую тему оформления.
        // Битая строка цвета в конфиге раньше роняла запуск — чистим на старте.
        bool colorsFixed = false;

        if (!Themes.TryParseColor(Settings.BgTopColor, out _)) { Settings.BgTopColor = ""; colorsFixed = true; }
        if (!Themes.TryParseColor(Settings.BgBottomColor, out _)) { Settings.BgBottomColor = ""; colorsFixed = true; }

        if (colorsFixed) { try { SaveState(); } catch { } }

        Themes.Apply(Settings.AccentTheme);

        // Фоновое автодополнение библиотеки из Steam (новые appmanifest).
        if (!smoke && !demoMode)
        {
            try { SteamLibraryWatcher.Start(); } catch { /* наблюдение не критично */ }
            if (Settings.AutoScanGames)
            {
                // Игры могли установить, пока лаунчер был выключен: полный поиск в фоне.
                _ = Task.Run(() => SteamLibraryWatcher.ScanAllAsync());
            }
        }

#if DEBUG
        // Отладочные режимы для проверки отрисовки окон. В релизной сборке их нет:
        // иначе флаг в ярлыке открывал бы админ-меню без пароля.
        if (TryRunDemo(e.Args)) return;
        if (TryRunShot(e.Args)) return;
#endif

        try
        {
            if (smoke)
            {
                // Самопроверка: готовим тестовое состояние автоматически.
                if (!Settings.PasswordSet)
                    ConfigStore.SetPassword(Settings, "smoke-test-pass");
                StartNewSession();
            }
            else if (!Settings.PasswordSet)
            {
                // Первый запуск: закрытая система — сначала создаём пароль администратора.
                var firstRun = new FirstRunWindow();
                if (firstRun.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
                StartNewSession();
            }
            else
            {
                if (!Settings.TimerEnabled)
                {
                    // Ограничение по времени выключено — блокировки по времени нет.
                    Settings.Locked = false;
                }
                else if (!Settings.Locked && Settings.SessionEverStarted && ComputeRemainingSeconds() <= 0)
                {
                    // Дедлайн мог истечь, пока лаунчер был закрыт: тогда нужен пароль, а не вход.
                    Settings.Locked = true;
                }

                // Обычный вход либо экран ограничения (время истекло → нужен пароль).
                var mode = Settings.Locked ? GateMode.Restricted : GateMode.Login;
                var gate = new LoginWindow(mode);
                if (gate.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }

                // Пароль введён — окно разблокировано (зелёный замок).
                AdminUnlocked = true;

                if (Settings.Locked)
                {
                    // Пароль введён → новый сеанс игры (последняя установленная длительность).
                    StartNewSession();
                }
                else
                {
                    // Не блокировано: продолжаем прежний сеанс с честным остатком.
                    Settings.RemainingSeconds = ComputeRemainingSeconds();
                }
            }

            ConfigStore.Save(Settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось запустить лаунчер:\n\n{ex.Message}",
                "TopRacerLauncher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var main = new MainWindow();
        MainWindow = main;
        main.Show();

        if (smoke)
        {
            // Режим самопроверки: показываем окна, прогоняем логические тесты и выходим.
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();

                // Проверяем, что XAML всех окон разбирается и окна создаются.
                var probeErrors = new List<string>();
                void Probe(string name, Func<Window> factory)
                {
                    try
                    {
                        var w = factory();
                        w.Close();
                    }
                    catch (Exception ex)
                    {
                        probeErrors.Add(name + ": " + ex.Message);
                    }
                }

                Probe("AdminWindow", () => new AdminWindow());
                Probe("ScanWindow", () => new ScanWindow());
                Probe("AddGameWindow", () => new AddGameWindow());
                Probe("FirstRunWindow", () => new FirstRunWindow(changeMode: true));
                Probe("LoginWindow", () => new LoginWindow(GateMode.AdminPrompt));
                Probe("CoverSearchWindow", () => new CoverSearchWindow(ImageKind.Cover, ""));
                Probe("ToastWindow", () => new ToastWindow(5, critical: true));
                Probe("LaunchOverlayWindow", () => new LaunchOverlayWindow("Test", GameControls.For("Counter-Strike 2")));
            Probe("ControlsOverlayWindow", () => new ControlsOverlayWindow("Test",
                GameControls.UsedKeys("Counter-Strike 2"), GameControls.For("Counter-Strike 2").Note));
                Probe("ChatWindow", () =>
                {
                    // Заодно проверяем карточку «Ты хочешь созвониться с другом?» на вкладке чата.
                    var chat = new ChatWindow();
                    chat.ShowTab(new LanPeer { Id = "smoke-chat", Name = "Тест" });
                    return chat;
                });
                Probe("CallWindow", () => new CallWindow());
                Probe("ChangelogWindow", () => new ChangelogWindow());
                Probe("IncomingCallWindow", () => new IncomingCallWindow("Тест"));
                Probe("VoiceOverlayWindow", () => new VoiceOverlayWindow());
                Probe("NoticeWindow", () => new NoticeWindow("Тест", "Текст уведомления"));
                Probe("UpdateWindow", () => new UpdateWindow(new UpdateInfo { Version = "9.9.9", Notes = "Тест" }, ""));
                Probe("AchievementsWindow", () => new AchievementsWindow());
                Probe("AchievementToast", () => new AchievementToast("\U0001F3C6", "Тест", "Проверка окна"));

                var report = SelfTest.Run();
                bool ok = report.Ok && probeErrors.Count == 0;
                var text = report.Text + Environment.NewLine +
                           (probeErrors.Count == 0
                               ? "OK   окна создаются"
                               : "FAIL окна: " + string.Join("; ", probeErrors));

                try
                {
                    var file = Path.Combine(ConfigStore.ConfigDirectory, ok ? "smoke-ok.txt" : "smoke-fail.txt");
                    File.WriteAllText(file, text, new System.Text.UTF8Encoding(true));
                }
                catch { /* не критично */ }

                Shutdown(ok ? 0 : 1);
            };
            timer.Start();
        }
    }

    private static bool HasArg(string[] args, string name) =>
        args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

#if DEBUG
    /// <summary>Демо-окна для отладки вёрстки. Только DEBUG-сборка.</summary>
    private bool TryRunDemo(string[] args)
    {
        bool scan = HasArg(args, "--demo-scan");
        bool cover = HasArg(args, "--demo-cover");
        bool main = HasArg(args, "--demo-main");
        bool admin = HasArg(args, "--demo-admin");
        bool call = HasArg(args, "--demo-call");
        bool chat = HasArg(args, "--demo-chat");
        bool achievements = HasArg(args, "--demo-achievements");
        bool changelog = HasArg(args, "--demo-changelog");
        if (!scan && !cover && !main && !admin && !call && !chat && !achievements && !changelog) return false;
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        Window demo = scan
            ? new ScanWindow()
            : cover
                ? new CoverSearchWindow(ImageKind.Cover, "Half-Life")
                : main
                    ? new MainWindow()
                    : call
                        ? new CallWindow()
                        : chat
                            ? new ChatWindow()
                            : achievements
                                ? new AchievementsWindow()
                                : changelog
                                    ? new ChangelogWindow()
                                    : new AdminWindow();
        demo.Show();

        if (chat)
        {
            ChatWindow.Open(new LanPeer { Id = "demo-a", Name = "Тестер", Game = "CS 2" });
            ChatWindow.Open(new LanPeer { Id = "demo-b", Name = "Второй друг", Game = "Dota 2" });
        }

        return true;
    }
#endif



#if DEBUG
    /// <summary>
    /// Отладочный скриншот окна (только DEBUG): --shot-admin=путь.png, --shot-main=путь.png.
    /// Окно создаётся за экраном и рисуется в PNG — удобно проверять вёрстку.
    /// </summary>
    private bool TryRunShot(string[] args)
    {
        string? Arg(string name) => args.FirstOrDefault(a => a.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        // Значение после "=" — путь к PNG; если это не путь (например, название игры), пишем в TEMP.
        static string PathOf(string arg)
        {
            var value = arg.Contains('=') ? arg[(arg.IndexOf('=') + 1)..] : "";
            return Path.IsPathRooted(value) && value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                ? value
                : Path.Combine(Path.GetTempPath(), "shot.png");
        }

        var mainArg = Arg("--shot-main");
        var adminArg = Arg("--shot-admin");
        var scrollArg = Arg("--shot-scroll");
        var overlayArg = Arg("--shot-overlay");
        var controlsArg = Arg("--shot-controls");
        if (mainArg == null && adminArg == null && overlayArg == null && controlsArg == null) return false;

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var originalFullScreen = Settings.FullScreen;
        if (mainArg != null) Settings.FullScreen = false;

        // --shot-bg=#14141A,#2B1030,diag: свой фон для скриншота (не сохраняется).
        var bgArg = Arg("--shot-bg");
        if (bgArg != null)
        {
            var value = bgArg.Contains('=') ? bgArg[(bgArg.IndexOf('=') + 1)..] : "";
            var parts = value.Split(',');
            if (parts.Length >= 2)
            {
                Settings.BgTopColor = parts[0].Trim();
                Settings.BgBottomColor = parts[1].Trim();
                Settings.BgPattern = parts.Length > 2 ? parts[2].Trim() : "";
            }
        }

        // --shot-theme=limecoal: посмотреть тему на скриншоте (не сохраняется).
        var themeArg = Arg("--shot-theme");
        if (themeArg != null)
        {
            var key = themeArg.Contains('=') ? themeArg[(themeArg.IndexOf('=') + 1)..] : "limecoal";
            Settings.AccentTheme = key;
            Themes.Apply(key);
        }

        var overlayTitle = overlayArg == null
            ? ""
            : overlayArg.Contains('=') ? overlayArg[(overlayArg.IndexOf('=') + 1)..] : "Counter-Strike 2";

        if (overlayArg != null) LaunchOverlayWindow.SuppressAutoCenter = true;

        var controlsTitle = controlsArg == null
            ? ""
            : controlsArg.Contains('=') ? controlsArg[(controlsArg.IndexOf('=') + 1)..] : "Counter-Strike 2";

        if (controlsArg != null) ControlsOverlayWindow.SuppressAutoPlace = true;

        Window window = controlsArg != null
            ? new ControlsOverlayWindow(controlsTitle, GameControls.UsedKeys(controlsTitle),
                GameControls.For(controlsTitle).Note)
            : overlayArg != null
            ? new LaunchOverlayWindow(overlayTitle, GameControls.For(overlayTitle))
            : mainArg != null
                ? new MainWindow { Width = 1600, Height = 900 }
                : new AdminWindow { Width = 1000, Height = 840 };


        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();

        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            if (window is MainWindow main)
            {
                main.WelcomeOverlay.Visibility = Visibility.Collapsed;

                var showDetails = typeof(MainWindow).GetMethod("ShowDetails",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var vm = Library.Items.FirstOrDefault();
                if (vm != null) showDetails?.Invoke(main, new object?[] { vm });
            }

            // --shot-tab=system: переключить раздел админки перед снимком.
            var tabArg = args.FirstOrDefault(a => a.StartsWith("--shot-tab", StringComparison.OrdinalIgnoreCase));
            if (window is AdminWindow adminWindow && tabArg != null)
            {
                var key = tabArg.Contains('=') ? tabArg[(tabArg.IndexOf('=') + 1)..] : "system";
                typeof(AdminWindow).GetMethod("SelectAdminTab",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(adminWindow, new object?[] { key });
            }

            if (scrollArg != null && int.TryParse(scrollArg.Split('=').Last(), out int offset))
            {
                try { FindScroller(window)?.ScrollToVerticalOffset(offset); } catch { }
            }

            var wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            wait.Tick += (_, _) =>
            {
                wait.Stop();

                try
                {
                    var root = (FrameworkElement)window.Content;
                    int w = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
                    int h = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));

                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(root);

                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));

                    var file = controlsArg != null ? Path.Combine(Path.GetTempPath(), "shot-controls.png")
                        : overlayArg != null ? PathOf(overlayArg)
                        : mainArg != null ? PathOf(mainArg)
                        : PathOf(adminArg!);
                    using var fs = File.Create(file);
                    encoder.Save(fs);
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "shot-error.txt"), ex.ToString()); } catch { }
                }

                Settings.FullScreen = originalFullScreen;
                Shutdown(0);
            };
            wait.Start();
        };
        timer.Start();

        return true;
    }
#endif
    private static System.Windows.Controls.ScrollViewer? FindScroller(DependencyObject root)
    {
        if (root is System.Windows.Controls.ScrollViewer sv &&
            sv.VerticalScrollBarVisibility != System.Windows.Controls.ScrollBarVisibility.Disabled)
            return sv;

        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindScroller(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }

        return null;
    }

    private static void LogCrash(string tag, object? exception)
    {
        try
        {
            File.AppendAllText(CrashLogPath,
                $"\n=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} {tag} ===\n{exception}\n");
        }
        catch { /* нет доступа к диску — не мешаем показу ошибки */ }
    }

    /// <summary>Остаток времени сеанса с учётом паузы и дедлайна.</summary>
    public static int ComputeRemainingSeconds(Settings? settings = null)
    {
        var s = settings ?? Settings;

        if (s.TimerPaused)
            return Math.Max(0, s.PausedRemainingSeconds);

        if (s.SessionEndUtc == default)
            return Math.Max(0, s.RemainingSeconds);

        var left = (s.SessionEndUtc - DateTime.UtcNow).TotalSeconds;
        return left <= 0 ? 0 : (int)Math.Ceiling(left);
    }

    /// <summary>Останавливает отсчёт, запоминая остаток.</summary>
    public static void PauseTimer()
    {
        var s = Settings;
        if (s.TimerPaused) return;

        s.PausedRemainingSeconds = ComputeRemainingSeconds(s);
        s.RemainingSeconds = s.PausedRemainingSeconds;
        s.TimerPaused = true;
    }

    /// <summary>Продолжает отсчёт с сохранённого остатка.</summary>
    public static void ResumeTimer()
    {
        var s = Settings;
        if (!s.TimerPaused) return;

        s.SessionEndUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, s.PausedRemainingSeconds));
        s.TimerPaused = false;
        s.RemainingSeconds = ComputeRemainingSeconds(s);
    }

    /// <summary>Выдаёт новый сеанс игры (таймер 30–240 минут из настроек).</summary>
    public static void StartNewSession()
    {
        int minutes = Math.Clamp(Settings.SessionMinutes, 30, 240);
        Settings.SessionMinutes = minutes;
        Settings.RemainingSeconds = minutes * 60;
        Settings.SessionEndUtc = DateTime.UtcNow.AddMinutes(minutes);
        Settings.PausedRemainingSeconds = 0;
        Settings.Locked = false;
        Settings.SessionEverStarted = true;
        Settings.TimerPaused = false;   // новый сеанс — снимаем паузу

        HistoryLog.Add("session_start", seconds: minutes * 60);
    }

    /// <summary>Продлевает текущий сеанс (кнопка «Продлить» в напоминании).</summary>
    public static void ExtendSession(int minutes)
    {
        if (minutes <= 0) return;

        var s = Settings;
        if (s.Locked) return;

        int extra = minutes * 60;

        if (s.TimerPaused)
        {
            s.PausedRemainingSeconds = Math.Max(0, s.PausedRemainingSeconds) + extra;
        }
        else
        {
            // Продлеваем от «сейчас», если время уже истекло, иначе — от дедлайна.
            var baseTime = ComputeRemainingSeconds(s) > 0 ? s.SessionEndUtc : DateTime.UtcNow;
            s.SessionEndUtc = baseTime.AddSeconds(extra);
        }

        s.SessionEverStarted = true;
        s.RemainingSeconds = ComputeRemainingSeconds(s);
        HistoryLog.Add("session_extend", seconds: extra, detail: "+" + minutes + " мин");
        SaveState();
    }

    /// <summary>Флаг: конфиг прочитан. До этого сохранять нельзя — затрём реальные данные.</summary>
    private static bool _settingsLoaded;

    /// <summary>Сохраняет состояние таймера (вызывается при закрытии и по событиям).</summary>
    public static void SaveState()
    {
        if (!_settingsLoaded) return;
        ConfigStore.Save(Settings);
    }

    /// <summary>Флаг: приложение завершается штатно (не блокируем закрытие окон).</summary>
    public static bool IsShuttingDown { get; private set; }

    /// <summary>Штатный выход из приложения.</summary>
    public static void RequestShutdown()
    {
        if (IsShuttingDown) return;
        IsShuttingDown = true;
        SaveState();
        Current.Shutdown();
    }

    /// <summary>
    /// Экран ограничения: пока не введён пароль — доступ закрыт.
    /// Возвращает true, если пароль введён (новый сеанс игры), false — выйти не удалось.
    /// </summary>
    public static bool ShowRestrictedGate(Window? owner)
    {
        var gate = new LoginWindow(GateMode.Restricted);
        if (owner != null)
        {
            gate.Owner = owner;
            gate.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        if (gate.ShowDialog() == true)
        {
            StartNewSession();
            SaveState();
            return true;
        }
        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { SaveState(); } catch { /* последний шанс */ }
        try { _instanceMutex?.ReleaseMutex(); } catch { /* уже отпущен */ }
        try { _instanceMutex?.Dispose(); } catch { /* ок */ }
        base.OnExit(e);
    }
}
