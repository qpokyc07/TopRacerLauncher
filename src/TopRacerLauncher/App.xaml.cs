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

        // Один экземпляр: два лаунчера = два таймера и гонка записи конфига.
        if (!smoke)
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
        if (!smoke) UsageStats.RecordLaunch();

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Применяем сохранённую тему оформления.
        Themes.Apply(Settings.AccentTheme);

#if DEBUG
        // Отладочные режимы для проверки отрисовки окон. В релизной сборке их нет:
        // иначе флаг в ярлыке открывал бы админ-меню без пароля.
        if (TryRunDemo(e.Args)) return;
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
                Probe("LaunchOverlayWindow", () => new LaunchOverlayWindow("Test"));
                Probe("CallWindow", () => new CallWindow());
                Probe("NoticeWindow", () => new NoticeWindow("Тест", "Текст уведомления"));
                Probe("UpdateWindow", () => new UpdateWindow(new UpdateInfo { Version = "9.9.9", Notes = "Тест" }, ""));

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
        if (!scan && !cover && !main && !admin && !call && !chat) return false;

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
                            ? new ChatWindow(new LanPeer { Id = "demo", Name = "Тестер", Game = "Counter-Strike 2" })
                            : new AdminWindow();
        demo.Show();
        return true;
    }
#endif

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
