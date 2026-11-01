using System.Windows;

namespace TopRacerLauncher;

/// <summary>Запуск голосового звонка с другом (общий код для главного окна и чата).</summary>
public static class CallLauncher
{
    private static CallWindow? _window;

    public static void Call(LanPeer peer, Window owner)
    {
        if (!VoiceEngine.InCall)
        {
            VoiceEngine.InputDeviceName = App.Settings.VoiceInputDevice;
            VoiceEngine.OutputDeviceName = App.Settings.VoiceOutputDevice;
            VoiceEngine.OpenMic = App.Settings.VoiceMode == "open";
            VoiceEngine.VoiceKey = App.Settings.VoiceKey;
            VoiceEngine.InputGain = App.Settings.VoiceInputGain;

            PttHook.Key = App.Settings.VoiceKey;

            if (!VoiceEngine.StartCall(Guid.NewGuid()))
            {
                MessageBox.Show(owner,
                    "Не удалось включить микрофон или занять порт звонка.\nПроверьте, что микрофон подключён.",
                    "Звонок", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            PttHook.Install();
        }

        LanPresence.SendCallInvite(peer, VoiceEngine.RoomId);
        ShowWindow(owner);
    }

    public static void ShowWindow(Window owner)
    {
        if (_window is { IsVisible: true })
        {
            _window.Activate();
            return;
        }

        _window = new CallWindow { Owner = owner };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }
}
