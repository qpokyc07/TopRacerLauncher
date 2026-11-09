using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TopRacerLauncher;

/// <summary>
/// Помогает игре выйти на экран после запуска: Roblox и некоторые клиенты стартуют
/// свёрнутыми или с окном за другим приложением. Находим окно процесса и разворачиваем.
/// </summary>
public static class GameWindow
{
    public static async Task<bool> BringToScreenAsync(Process process, TimeSpan timeout, bool fullscreen = true)
    {
        var until = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < until)
        {
            try
            {
                if (process.HasExited) return false;
            }
            catch { return false; }

            var window = FindMainWindow((uint)process.Id);
            if (window != IntPtr.Zero)
            {
                ShowWindow(window, SwRestore);
                ShowWindow(window, SwMaximize);
                SetForegroundWindow(window);

                if (fullscreen)
                {
                    await Task.Delay(700);
                    ToggleFullscreen();
                }

                return true;
            }

            await Task.Delay(1000);
        }

        return false;
    }

    /// <summary>Ищет видимое окно процесса, пропуская служебные окна вроде «Default IME».</summary>
    private static IntPtr FindMainWindow(uint processId)
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != processId) return true;
            if (!IsWindowVisible(hWnd)) return true;

            var title = new StringBuilder(256);
            GetWindowText(hWnd, title, 256);

            var text = title.ToString();
            if (text.Length == 0) return true;
            if (text.Contains("Default IME", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Contains("MSCTFIME", StringComparison.OrdinalIgnoreCase)) return true;

            found = hWnd;
            return false;
        }, IntPtr.Zero);

        return found;
    }

    /// <summary>Alt+Enter — переключение полноэкранного режима в большинстве игр.</summary>
    private static void ToggleFullscreen()
    {
        keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
        keybd_event(VkReturn, 0, 0, UIntPtr.Zero);
        keybd_event(VkReturn, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkMenu, 0, KeyUp, UIntPtr.Zero);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    private const int SwRestore = 9;
    private const int SwMaximize = 3;
    private const byte VkMenu = 0x12;
    private const byte VkReturn = 0x0D;
    private const uint KeyUp = 2;
}
