using System.Runtime.InteropServices;

namespace TopRacerLauncher;

/// <summary>
/// Глобальный перехват клавиши Push-to-Talk: работает и когда фокус в игре,
/// поэтому «рация» срабатывает поверх полноэкранного окна.
/// </summary>
public static class PttHook
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private static IntPtr _hook = IntPtr.Zero;
    private static LowLevelKeyboardProc? _proc;

    /// <summary>Виртуальный код клавиши (по умолчанию V).</summary>
    public static int Key { get; set; } = 0x56;

    public static void Install()
    {
        if (_hook != IntPtr.Zero) return;

        _proc = HookCallback;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(null), 0);
    }

    public static void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        _proc = null;
        VoiceEngine.PushToTalkHeld = false;
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            if (vk == Key)
            {
                int msg = wParam.ToInt32();
                if (msg is WmKeyDown or WmSysKeyDown) VoiceEngine.PushToTalkHeld = true;
                else if (msg is WmKeyUp or WmSysKeyUp) VoiceEngine.PushToTalkHeld = false;
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
