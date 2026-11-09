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
    }

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

        main.Topmost = IsOwnWindowForeground(main) || GameRunning();
    }

    private static bool GameRunning()
    {
        try { return App.Runner.IsRunning; }
        catch { return false; }
    }

    private static void OnTick(object? sender, EventArgs e) => Refresh();

    /// <summary>Активно ли окно лаунчера (или рабочий стол), а не сторонняя программа.</summary>
    private static bool IsOwnWindowForeground(Window main)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return true;

        GetWindowThreadProcessId(foreground, out int pid);
        if (pid == 0) return true;

        if (pid == Environment.ProcessId) return true;

        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName;

            // Рабочий стол, проводник и панель задач — это «свой» экран.
            if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { }

        return false;
    }

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
