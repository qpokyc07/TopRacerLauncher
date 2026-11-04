using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TopRacerLauncher;

/// <summary>Запуск и принудительное завершение игр.</summary>
public sealed class GameRunner : IDisposable
{
    private const int SwHide = 0;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private Process? _process;
    private IntPtr _job;
    private string? _exePath;

    /// <summary>Процессы лаунчеров магазинов, у которых прячем окно при запуске игры.</summary>
    private static readonly string[] StoreWindowNames =
    {
        "steam", "epicgameslauncher", "galaxyclient", "ubisoftconnect", "origin", "eadesktop", "battle.net",
    };

    private static System.Threading.Timer? _hideTimer;
    private static int _hideTicksLeft;

    public string? CurrentTitle { get; private set; }

    private DateTime? _sessionStart;
    private string? _sessionTitle;
    public string? CurrentExePath { get; private set; }

    /// <summary>Игра запущена напрямую (exe), а не через оболочку магазина.</summary>
    public bool HasDirectProcess => _process != null;

    /// <summary>Сколько ждём появления процесса игры после запуска (Steam/лаунчеры стартуют не сразу).</summary>
    private static readonly TimeSpan LaunchGrace = TimeSpan.FromSeconds(60);

    private DateTime _startedUtc;
    private bool _seenRunning;

    public bool IsRunning
    {
        get
        {
            try
            {
                if (_process is { HasExited: false }) return true;
            }
            catch { /* процесс мог завершиться */ }

            // Игру, запущенную через Steam, видно только по её собственному exe.
            return IsProcessPathRunning(_exePath);
        }
    }

    /// <summary>Сессия закончилась: игра больше не работает (с запасом на долгий старт).</summary>
    public bool HasSessionEnded()
    {
        if (_sessionStart == null) return false;

        if (IsRunning)
        {
            _seenRunning = true;
            return false;
        }

        if (_seenRunning) return true;

        // Игру ещё ни разу не видели — даём время на запуск через Steam/лаунчер.
        return DateTime.UtcNow - _startedUtc > LaunchGrace;
    }

    /// <summary>Есть ли процесс ровно по этому пути (сравнение по полному пути, не по имени).</summary>
    private static bool IsProcessPathRunning(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return false;

        try
        {
            var name = Path.GetFileNameWithoutExtension(exePath);
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { /* нет доступа к модулю — пропускаем */ }
                finally { process.Dispose(); }
            }
        }
        catch { /* нет доступа к списку процессов */ }

        return false;
    }

    /// <summary>Запускает игру. Бросает исключение с понятным сообщением при ошибке.</summary>
    public void Start(GameItem game)
    {
        KillCurrent();

        var target = Resolve(game);
        bool direct = target.IsDirect;

        if (direct && !File.Exists(target.Path))
            throw new FileNotFoundException($"Файл игры не найден:\n{target.Path}");

        Process proc;
        if (direct)
        {
            var psi = new ProcessStartInfo
            {
                FileName = target.Path,
                Arguments = target.Args,
                WorkingDirectory = target.WorkDir,
                UseShellExecute = false,   // нужен handle процесса, чтобы привязать его к Job Object
            };

            try
            {
                proc = Process.Start(psi)
                       ?? throw new InvalidOperationException("Не удалось запустить игру.");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 740)
            {
                // Игра требует прав администратора: Direct-запуск невозможен, идём через shell (UAC).
                // Job Object в этом случае недоступен — игру закроем по пути к exe.
                proc = StartViaShell(target);
                direct = false;
            }

            if (direct)
            {
                _job = NativeJob.CreateKillOnCloseJob();
                if (_job != IntPtr.Zero)
                {
                    try
                    {
                        if (!NativeJob.AssignProcessToJobObject(_job, proc.Handle))
                        {
                            NativeJob.CloseHandle(_job);
                            _job = IntPtr.Zero;
                        }
                    }
                    catch
                    {
                        NativeJob.CloseHandle(_job);
                        _job = IntPtr.Zero;
                    }
                }
            }
        }
        else
        {
            proc = StartViaShell(target);
        }

        // Для Steam-игр exe самой игры запоминаем отдельно — по нему закрываем сессию.
        _exePath = target.GameExe;
        _process = proc;
        CurrentExePath = target.GameExe ?? target.Path;
        CurrentTitle = game.Title;

        _sessionStart = DateTime.UtcNow;
        _sessionTitle = game.Title;
        _startedUtc = DateTime.UtcNow;
        _seenRunning = false;

        HistoryLog.Add("game_start", title: game.Title, detail: target.Path);

        // Steam/Rockstar/Epic могут показать своё окно — прячем его, если включено в настройках.
        StartHidingStoreWindows();
    }

    /// <summary>Прячет окна лаунчеров магазинов несколько секунд после запуска игры.</summary>
    public static void StartHidingStoreWindows(int seconds = 25)
    {
        if (!App.Settings.HideStoreWindows) return;

        _hideTicksLeft = seconds;
        _hideTimer ??= new System.Threading.Timer(_ => HideTick(), null, Timeout.Infinite, Timeout.Infinite);
        _hideTimer.Change(0, 1000);
    }

    private static void HideTick()
    {
        try
        {
            HideStoreWindows();
            if (Interlocked.Decrement(ref _hideTicksLeft) <= 0)
                _hideTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch { /* прятать нечего */ }
    }

    /// <summary>Скрывает главные окна Steam, Rockstar, Epic, GOG, Ubisoft, EA и Battle.net.</summary>
    public static int HideStoreWindows()
    {
        int hidden = 0;

        foreach (var p in SafeGetProcesses())
        {
            try
            {
                if (p.Id == Environment.ProcessId) continue;

                var name = p.ProcessName.ToLowerInvariant();
                bool match = StoreWindowNames.Contains(name);

                if (!match)
                {
                    var path = p.MainModule?.FileName?.ToLowerInvariant();
                    if (path != null && path.Contains("\\rockstar games\\", StringComparison.Ordinal))
                    {
                        // Только сам лаунчер Rockstar, не exe игр.
                        var file = Path.GetFileName(path);
                        match = file is "launcher.exe" or "rockstarerrorhandler.exe" or "socialclubhelper.exe"
                                || path.Contains("\\launcher\\", StringComparison.Ordinal);
                    }
                }

                if (!match) continue;

                var handle = p.MainWindowHandle;
                if (handle == IntPtr.Zero) continue;

                if (ShowWindow(handle, SwHide)) hidden++;
            }
            catch { /* недоступный процесс — пропускаем */ }
        }

        return hidden;
    }

    /// <summary>Запуск через shell: ярлыки, .bat, URL-схемы и игры, требующие UAC.</summary>
    private static Process StartViaShell(LaunchTarget target)
    {
        var psi = new ProcessStartInfo
        {
            FileName = target.Path,
            Arguments = target.Args,
            WorkingDirectory = target.WorkDir,
            UseShellExecute = true,
        };

        return Process.Start(psi)
               ?? throw new InvalidOperationException("Не удалось запустить игру.");
    }

    /// <summary>Завершает запущенную игру (Job Object гарантирует всё дерево процессов).</summary>
    public bool KillCurrent()
    {
        bool wasRunning = IsRunning;
        bool killed = false;

        // 1) Job Object: всё дерево, включая дочерние процессы, «оторвавшиеся» от игры.
        if (_job != IntPtr.Zero)
        {
            try
            {
                if (NativeJob.TerminateJobObject(_job, 1)) killed = true;
            }
            catch { /* job уже закрыт */ }

            try { NativeJob.CloseHandle(_job); } catch { /* ок */ }
            _job = IntPtr.Zero;
        }

        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                killed = true;
            }
        }
        catch { /* процесс мог завершиться сам */ }

        // 2) Страховка: игра могла перезапустить себя отдельным процессом.
        // Совпадение только по полному пути — иначе можно убить чужое приложение с тем же именем.
        var exe = _exePath;
        if (!string.IsNullOrEmpty(exe))
        {
            foreach (var p in SafeGetProcesses())
            {
                try
                {
                    if (p.Id == Environment.ProcessId || p.HasExited) continue;
                    if (!string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) continue;
                    p.Kill(entireProcessTree: true);
                    killed = true;
                }
                catch { /* недоступный системный процесс — пропускаем */ }
            }
        }

        _process?.Dispose();
        _process = null;
        _exePath = null;

        if ((killed || wasRunning) && !string.IsNullOrEmpty(CurrentTitle))
            HistoryLog.Add("game_kill", title: CurrentTitle);

        CompleteSession();
        return killed || wasRunning;
    }

    /// <summary>Фиксирует длительность текущей сессии в статистике (если игра уже завершилась).</summary>
    public void CompleteSession()
    {
        if (_sessionStart == null || string.IsNullOrEmpty(_sessionTitle)) return;

        var duration = DateTime.UtcNow - _sessionStart.Value;
        var title = _sessionTitle;

        _sessionStart = null;
        _sessionTitle = null;

        try { PlayStats.Add(title, duration); } catch { /* статистика не критична */ }

        // Сессия записана — проверяем, не открылось ли достижение.
        try { Achievements.Evaluate(); } catch { /* достижения не критичны */ }
    }

    private static IEnumerable<Process> SafeGetProcesses()
    {
        try { return Process.GetProcesses(); }
        catch { return Array.Empty<Process>(); }
    }

    /// <summary>Что и как запускать.</summary>
    /// <param name="Path">Что запускаем (игра или steam.exe).</param>
    /// <param name="Args">Аргументы.</param>
    /// <param name="WorkDir">Рабочая папка.</param>
    /// <param name="IsDirect">Прямой запуск exe (можно привязать к Job Object).</param>
    /// <param name="GameExe">exe самой игры — по нему завершаем сессию, даже если стартовали через Steam.</param>
    internal readonly record struct LaunchTarget(string Path, string Args, string WorkDir, bool IsDirect, string? GameExe = null);

    /// <summary>
    /// Steam-игру нельзя запускать напрямую: без Steam-контекста VAC считает файлы «без подписи»
    /// и не пускает на защищённые серверы. Возвращает appid, если игра лежит в steamapps.
    /// </summary>
    private static bool TryGetSteamAppId(GameItem game, string exePath, out string appId)
    {
        appId = "";

        const string marker = @"\steamapps\common\";
        int index = exePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return false;

        // 1) appid уже есть в карточке игры (так его сохраняет сканер Steam).
        var known = game.AppId?.Trim() ?? "";
        if (known.Length > 0 && known.All(char.IsDigit))
        {
            appId = known;
            return true;
        }

        // 2) иначе ищем манифест по папке установки: steamapps\appmanifest_<id>.acf.
        try
        {
            var steamApps = exePath[..(index + @"\steamapps".Length)];
            var tail = exePath[(index + marker.Length)..];
            var installDir = tail.Split('\\')[0];

            foreach (var acf in Directory.GetFiles(steamApps, "appmanifest_*.acf"))
            {
                var content = File.ReadAllText(acf);
                var dir = Regex.Match(content, "\"installdir\"\\s*\"([^\"]+)\"").Groups[1].Value;

                if (!string.Equals(dir, installDir, StringComparison.OrdinalIgnoreCase)) continue;

                var id = Regex.Match(content, "\"appid\"\\s*\"(\\d+)\"").Groups[1].Value;
                if (id.Length > 0)
                {
                    appId = id;
                    return true;
                }
            }
        }
        catch { /* нет доступа к манифестам — запустим напрямую */ }

        return false;
    }

    /// <summary>Разрешает путь игры: .exe запускаем напрямую, остальное — через shell.</summary>
    /// <summary>Ищет steam.exe в реестре и стандартных папках.</summary>
    internal static string? FindSteamExe()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamExe") is string path && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return path;
        }
        catch { /* нет доступа к реестру */ }

        foreach (var candidate in new[]
                 {
                     @"C:\Program Files (x86)\Steam\steam.exe",
                     @"C:\Program Files\Steam\steam.exe",
                 })
        {
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    internal static LaunchTarget Resolve(GameItem game)
    {
        var raw = game.ExecutablePath.Trim().Trim('"');
        string args = game.Arguments?.Trim() ?? "";
        string workDir = "";

        // URL-схемы (steam://, goggalaxy://, …) — только shell.
        // Steam-игры надёжнее запускать через steam.exe -applaunch: URL-ассоциация
        // иногда приводит к проверкам сертификатов и отказу запуска.
        if (raw.Contains("://", StringComparison.Ordinal))
        {
            var steamId = Regex.Match(raw, @"steam://rungameid/(\d+)", RegexOptions.IgnoreCase);
            var foundSteam = FindSteamExe();
            if (steamId.Success && foundSteam != null)
            {
                return new LaunchTarget(foundSteam, "-applaunch " + steamId.Groups[1].Value,
                    Path.GetDirectoryName(foundSteam) ?? "", IsDirect: false);
            }

            return new LaunchTarget(raw, args, "", IsDirect: false);
        }

        if (string.Equals(Path.GetExtension(raw), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            // Читаем цель ярлыка через WScript.Shell.
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                object? shell = null, lnk = null;
                try
                {
                    shell = Activator.CreateInstance(shellType);
                    dynamic dynamicShell = shell!;
                    lnk = dynamicShell.CreateShortcut(raw);
                    dynamic shortcut = lnk;
                    string target = (string)(shortcut.TargetPath ?? "");
                    string lnkArgs = (string)(shortcut.Arguments ?? "");
                    string lnkWork = (string)(shortcut.WorkingDirectory ?? "");

                    if (string.IsNullOrWhiteSpace(target))
                        throw new InvalidOperationException($"Ярлык не содержит путь к игре:\n{raw}");

                    if (string.IsNullOrEmpty(args)) args = lnkArgs;
                    workDir = lnkWork;
                    raw = target;
                }
                finally
                {
                    if (lnk != null) { try { Marshal.ReleaseComObject(lnk); } catch { } }
                    if (shell != null) { try { Marshal.ReleaseComObject(shell); } catch { } }
                }
            }
        }

        bool isExe = string.Equals(Path.GetExtension(raw), ".exe", StringComparison.OrdinalIgnoreCase);

        if (isExe)
        {
            if (string.IsNullOrEmpty(workDir))
                workDir = Path.GetDirectoryName(raw) ?? "";

            // Steam-игру запускаем через steam.exe -applaunch: иначе VAC не видит подписей файлов.
            var steamExe = FindSteamExe();
            if (steamExe != null && TryGetSteamAppId(game, raw, out var steamAppId))
            {
                return new LaunchTarget(steamExe, "-applaunch " + steamAppId,
                    Path.GetDirectoryName(steamExe) ?? "", IsDirect: false, GameExe: raw);
            }

            return new LaunchTarget(raw, args, workDir, IsDirect: true, GameExe: raw);
        }

        if (string.IsNullOrEmpty(workDir) && Path.IsPathRooted(raw))
            workDir = Path.GetDirectoryName(raw) ?? "";

        return new LaunchTarget(raw, args, workDir, IsDirect: false);
    }

    public void Dispose() => KillCurrent();
}

/// <summary>Job Object: гарантирует, что игра и её дочерние процессы умрут вместе с нами.</summary>
internal static class NativeJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int infoClass, IntPtr lpInfo, uint cbInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Создаёт job, который убивает всех участников при закрытии handle.</summary>
    public static IntPtr CreateKillOnCloseJob()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;

        var info = new ExtendedLimitInformation();
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

        int size = Marshal.SizeOf<ExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size))
            {
                CloseHandle(job);
                return IntPtr.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return job;
    }
}

/// <summary>Автозагрузка лаунчера при включении ПК.</summary>
public static class StartupManager
{
    private const string ShortcutName = "TopRacerLauncher.lnk";

    public static string StartupFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs\Startup");

    public static string ShortcutPath => Path.Combine(StartupFolder, ShortcutName);

    public static bool IsEnabled() => File.Exists(ShortcutPath);

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            // В одиночном exe Environment.ProcessPath надёжнее Assembly.Location.
            var exe = Environment.ProcessPath
                      ?? Path.Combine(AppContext.BaseDirectory, "TopRacerLauncher.exe");
            Directory.CreateDirectory(StartupFolder);

            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                            ?? throw new InvalidOperationException("Не найден WScript.Shell.");
            object? shell = null, lnk = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                dynamic dynamicShell = shell!;
                lnk = dynamicShell.CreateShortcut(ShortcutPath);
                dynamic shortcut = lnk;
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? "";
                shortcut.WindowStyle = 1;
                shortcut.Description = "TopRacerLauncher — игровой лаунчер";
                shortcut.Save();
            }
            finally
            {
                if (lnk != null) { try { Marshal.ReleaseComObject(lnk); } catch { } }
                if (shell != null) { try { Marshal.ReleaseComObject(shell); } catch { } }
            }
        }
        else if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }
}
