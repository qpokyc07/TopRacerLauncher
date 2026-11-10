using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>
/// Киоск-режим «по умолчанию сверху, но не мешает приложениям»:
/// пока активен сам лаунчер — он держится поверх окон; как только пользователь
/// запустил игру или программу, лаунчер уступает ей передний план.
/// Служебные лончеры (Rockstar Games Launcher, Social Club) при этом прячутся:
/// они нужны только для запуска игры и не должны висеть на экране.
/// </summary>
public static class KioskGuard
{
    // Лончеры, которые прячем: их окна не должны появляться у гостя.
    private static readonly string[] HiddenLaunchers =
    {
        "rockstar games", "social club", "rockstarservice", "rockstar games launcher",
        "ubisoft connect", "ubisoftgameLauncher", "eadesktop", "ealauncher",
    };

    private static readonly DispatcherTimer Timer = new() { Interval = TimeSpan.FromMilliseconds(1200) };
    private static Window? _window;
    private static readonly HashSet<int> _hidden = new();

    // Пока идёт «уступка» (запуск игры), лаунчер не лезет наверх.
    private static DateTime _yieldUntil;
    private static bool _wasMaximized;
    private static bool _yieldMinimized;
    private static bool _gameEnded;

    public static void Start(Window window)
    {
        _window = window;
        Timer.Tick -= OnTick;
        Timer.Tick += OnTick;
        Timer.Start();
        Refresh();
    }

    public static void Stop()
    {
        Timer.Stop();
        _window = null;
        _hidden.Clear();
        _yieldUntil = default;
    }

    /// <summary>
    /// Уступить экран запускаемой игре: сворачиваем лаунчер, чтобы игра точно
    /// получила передний план (важно для Roblox из Microsoft Store и лончеров).
    /// </summary>
    public static void Yield(int seconds = 25)
    {
        _yieldUntil = DateTime.UtcNow.AddSeconds(seconds);

        var main = _window;
        if (main == null) return;

        try
        {
            if (main.WindowState != WindowState.Minimized)
            {
                _wasMaximized = main.WindowState == WindowState.Maximized;
                _yieldMinimized = true;
            }

            main.Topmost = false;
            main.WindowState = WindowState.Minimized;
        }
        catch { }
    }

    /// <summary>Игра закрылась: лаунчер вернётся, когда экран освободится.</summary>
    public static void NotifyGameEnded() => _gameEnded = true;

    /// <summary>Идёт ли сейчас уступка экрана игре.</summary>
    public static bool IsYielding => DateTime.UtcNow < _yieldUntil;

    /// <summary>Пересчитывает «поверх окон» и прячет служебные лончеры.</summary>
    public static void Refresh()
    {
        var main = _window;
        if (main == null) return;

        try { HideServiceLaunchers(); } catch { }

        if (!App.Settings.KioskMode)
        {
            if (main.Topmost) main.Topmost = false;
            return;
        }

        // Идёт запуск игры или игра работает — не мешаем ей ни секунды.
        if (IsYielding || GameRunning())
        {
            if (main.Topmost) main.Topmost = false;
            return;
        }

        // Лаунчер свёрнут после запуска: возвращаемся, только когда экран свободен —
        // либо пользователь сам вернулся в лаунчер, либо игра закрылась и виден рабочий стол.
        if (_yieldMinimized)
        {
            bool launcherActive = IsLauncherForeground();
            bool screenFree = IsDesktopForeground();

            if (launcherActive || (_gameEnded && screenFree))
            {
                _yieldMinimized = false;
                _gameEnded = false;

                try
                {
                    main.WindowState = _wasMaximized ? WindowState.Maximized : WindowState.Normal;
                    main.Topmost = true;
                }
                catch { }

                return;
            }

            if (main.Topmost) main.Topmost = false;
            return;
        }

        // Обычный режим киоска: поверх окон, пока активно окно лаунчера.
        main.Topmost = IsLauncherForeground();
    }

    private static bool GameRunning()
    {
        try { return App.Runner.IsRunning; }
        catch { return false; }
    }

    private static void OnTick(object? sender, EventArgs e) => Refresh();

    /// <summary>На переднем плане чужая программа (игра, лончер, браузер)?</summary>
    public static bool ForeignWindowActive()
    {
        var main = _window;
        if (main == null) return false;

        try { return !IsLauncherForeground() && !IsDesktopForeground(); }
        catch { return false; }
    }

    /// <summary>Активно ли именно окно лаунчера.</summary>
    private static bool IsLauncherForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return true;

        GetWindowThreadProcessId(foreground, out int pid);
        return pid == Environment.ProcessId;
    }

    /// <summary>На переднем плане рабочий стол или проводник.</summary>
    private static bool IsDesktopForeground()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return true;

        GetWindowThreadProcessId(foreground, out int pid);
        if (pid == Environment.ProcessId) return true;

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch { return true; }
    }

    /// <summary>Активно ли окно лаунчера (или рабочий стол), а не сторонняя программа.</summary>


    /// <summary>Прячет окна служебных лончеров (Rockstar, Ubisoft, EA).</summary>
    private static void HideServiceLaunchers()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (_hidden.Contains(process.Id)) continue;
                if (process.MainWindowHandle == IntPtr.Zero) continue;

                var path = (process.MainModule?.FileName ?? "").ToLowerInvariant();
                var title = (process.MainWindowTitle ?? "").ToLowerInvariant();

                bool hide = HiddenLaunchers.Any(word => path.Contains(word) || title.Contains(word));
                if (!hide) continue;

                ShowWindow(process.MainWindowHandle, SwHide);
                _hidden.Add(process.Id);
            }
            catch { /* чужой процесс — пропускаем */ }
            finally
            {
                process.Dispose();
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SwHide = 0;
}
