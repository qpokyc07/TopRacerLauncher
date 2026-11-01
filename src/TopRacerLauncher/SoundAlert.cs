using System.Runtime.InteropServices;

namespace TopRacerLauncher;

/// <summary>
/// Короткие системные звуки. MessageBeep играет через стандартную схему Windows,
/// не требует ссылок на WinForms/WPF-медиа и не блокирует UI.
/// </summary>
public static class SoundAlert
{
    private const uint MbIconHand = 0x00000010;
    private const uint MbIconQuestion = 0x00000020;
    private const uint MbIconExclamation = 0x00000030;
    private const uint MbIconAsterisk = 0x00000040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MessageBeep(uint uType);

    /// <summary>Мягкий сигнал (предупреждение о времени).</summary>
    public static void Info()
    {
        if (!Settings().SoundAlerts) return;
        Beep(MbIconAsterisk);
    }

    /// <summary>Заметный сигнал (осталось мало времени).</summary>
    public static void Warn()
    {
        if (!Settings().SoundAlerts) return;
        Beep(MbIconExclamation);
    }

    /// <summary>Тревожный сигнал: кто-то пытается подобрать пароль.</summary>
    public static void Denied()
    {
        if (!Settings().SoundAlerts) return;
        Beep(MbIconHand);
    }

    private static Settings Settings() => App.Settings;

    private static void Beep(uint type)
    {
        try { MessageBeep(type); }
        catch { /* нет звуковой схемы — не критично */ }
    }
}
