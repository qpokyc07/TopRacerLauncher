using System.Runtime.InteropServices;

namespace TopRacerLauncher;

/// <summary>
/// Киоск-режим для клуба: прячет панель задач и перехватывает клавиши выхода
/// (Win, Alt+Tab, Alt+Esc, Alt+F4, Ctrl+Esc), чтобы посетитель не покинул лаунчер.
/// </summary>
public static class KioskMode
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;

    private const int VkTab = 0x09;
    private const int VkEscape = 0x1B;
    private const int VkMenu = 0x12;     // Alt
    private const int VkControl = 0x11;  // Ctrl
    private const int VkF4 = 0x73;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelKeyboardProc? _proc;

    public static bool Enabled { get; private set; }

    public static void Enable()
    {
        if (Enabled) return;
        Enabled = true;

        HideTaskbar(true);

        _proc = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
    }

    public static void Disable()
    {
        if (!Enabled) return;
        Enabled = false;

        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _proc = null;
        }

        HideTaskbar(false);
    }

    public static void Apply(bool enabled)
    {
        if (enabled) Enable();
        else Disable();
    }

    private static void HideTaskbar(bool hide)
    {
        foreach (var cls in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            var hwnd = FindWindow(cls, null);
            if (hwnd != IntPtr.Zero) ShowWindow(hwnd, hide ? 0 : 5);   // SW_HIDE / SW_SHOW
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            int msg = wParam.ToInt32();

            // Блокируем только нажатия — отпускания пропускаем, чтобы не «залипали» клавиши.
            if (msg is WmKeyDown or WmSysKeyDown && IsBlocked(vk))
                return (IntPtr)1;
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsBlocked(int vk)
    {
        // Клавиша Windows (меню «Пуск», Win+D, Win+L и т.п.)
        if (vk is VkLWin or VkRWin) return true;

        bool alt = (GetAsyncKeyState(VkMenu) & 0x8000) != 0;
        bool ctrl = (GetAsyncKeyState(VkControl) & 0x8000) != 0;

        if (alt && (vk is VkTab or VkEscape or VkF4)) return true;   // Alt+Tab, Alt+Esc, Alt+F4
        if (ctrl && vk is VkEscape) return true;                     // Ctrl+Esc

        return false;
    }
}
