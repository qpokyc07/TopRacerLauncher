using System.Windows;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>Окно звонка: участники, выбор микрофона/наушников, режим PTT.</summary>
public partial class CallWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private List<string> _micDevices = new();
    private List<string> _spkDevices = new();

    public CallWindow()
    {
        InitializeComponent();

        _micDevices = VoiceEngine.GetInputDevices();
        _spkDevices = VoiceEngine.GetOutputDevices();

        RefreshLabels();
        RefreshParticipants();
        UpdateTalkBadge();

        _timer.Tick += (_, _) =>
        {
            RefreshParticipants();
            UpdateTalkBadge();
        };
        _timer.Start();

        VoiceEngine.StateChanged += OnVoiceStateChanged;
        Closed += (_, _) =>
        {
            _timer.Stop();
            VoiceEngine.StateChanged -= OnVoiceStateChanged;
        };
    }

    private void OnVoiceStateChanged()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshLabels();
            RefreshParticipants();
            UpdateTalkBadge();

            if (!VoiceEngine.InCall) Close();
        }));
    }

    // ---------- Участники ----------

    private void RefreshParticipants()
    {
        if (!VoiceEngine.InCall)
        {
            ParticipantsList.ItemsSource = null;
            CountText.Text = "0/4";
            return;
        }

        var me = new
        {
            Name = LanPresence.SelfName + " (вы)",
            Status = VoiceEngine.OpenMic ? "микрофон открыт" : "по кнопке",
        };

        var others = LanPresence.GetPeers()
            .Where(p => string.Equals(p.Room, VoiceEngine.RoomId.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(p => new { p.Name, Status = "в звонке" })
            .ToList();

        var list = new List<object> { me };
        list.AddRange(others);

        ParticipantsList.ItemsSource = list;
        CountText.Text = $"{list.Count}/4";
    }

    private void UpdateTalkBadge()
    {
        bool talking = VoiceEngine.IsTransmitting;

        TalkText.Text = talking
            ? "Передача идёт — вас слышно"
            : VoiceEngine.OpenMic
                ? "Микрофон выключен"
                : $"Удерживайте {(char)VoiceEngine.VoiceKey} для передачи";
        TalkBadge.Background = talking
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x14, 0x3D, 0x2C))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x2A, 0x22));
        TalkBadge.BorderBrush = talking
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x5B))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x3A, 0x32));
        TalkText.Foreground = talking
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0xE0, 0xB5))
            : FindResource("TextSecondaryBrush") as System.Windows.Media.Brush
              ?? System.Windows.Media.Brushes.Gray;
    }

    // ---------- Устройства ----------

    private void RefreshLabels()
    {
        var mic = VoiceEngine.InputDeviceName;
        var spk = VoiceEngine.OutputDeviceName;

        MicButton.Content = "Микрофон: " + (string.IsNullOrWhiteSpace(mic) ? "по умолчанию" : mic) + "  ▾";
        SpkButton.Content = "Наушники: " + (string.IsNullOrWhiteSpace(spk) ? "по умолчанию" : spk) + "  ▾";
        ModeButton.Content = VoiceEngine.OpenMic
            ? "Режим: постоянно"
            : $"Режим: по кнопке ({(char)VoiceEngine.VoiceKey})";
    }

    private void MicButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = MicButton };
        AddDeviceItem(menu, "Системный по умолчанию", "", VoiceEngine.InputDeviceName,
            value =>
            {
                VoiceEngine.InputDeviceName = value;
                App.Settings.VoiceInputDevice = value;
            });

        foreach (var device in _micDevices)
        {
            var name = device;
            AddDeviceItem(menu, name, name, VoiceEngine.InputDeviceName, value =>
            {
                VoiceEngine.InputDeviceName = value;
                App.Settings.VoiceInputDevice = value;
            });
        }

        menu.IsOpen = true;
    }

    private void SpkButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = SpkButton };
        AddDeviceItem(menu, "Системное по умолчанию", "", VoiceEngine.OutputDeviceName,
            value =>
            {
                VoiceEngine.OutputDeviceName = value;
                App.Settings.VoiceOutputDevice = value;
            });

        foreach (var device in _spkDevices)
        {
            var name = device;
            AddDeviceItem(menu, name, name, VoiceEngine.OutputDeviceName, value =>
            {
                VoiceEngine.OutputDeviceName = value;
                App.Settings.VoiceOutputDevice = value;
            });
        }

        menu.IsOpen = true;
    }

    private void AddDeviceItem(System.Windows.Controls.ContextMenu menu, string header,
        string value, string currentValue, Action<string> apply)
    {
        var item = new System.Windows.Controls.MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = string.Equals(currentValue, value, StringComparison.OrdinalIgnoreCase),
        };

        item.Click += (_, _) =>
        {
            apply(value);
            App.SaveState();
            RefreshLabels();
            RestartVoice();
        };

        menu.Items.Add(item);
    }
    private void RestartVoice()
    {
        if (!VoiceEngine.InCall) return;
        var room = VoiceEngine.RoomId;
        VoiceEngine.LeaveCall();
        VoiceEngine.StartCall(room);
    }

    private void ToggleMode_Click(object sender, RoutedEventArgs e)
    {
        VoiceEngine.OpenMic = !VoiceEngine.OpenMic;
        App.Settings.VoiceMode = VoiceEngine.OpenMic ? "open" : "ptt";
        App.SaveState();
        RefreshLabels();
        UpdateTalkBadge();
    }

    // ---------- Кнопки ----------

    private void Invite_Click(object sender, RoutedEventArgs e)
    {
        if (!VoiceEngine.InCall) return;

        // В комнате не больше 4 человек, включая себя.
        var roomText = VoiceEngine.RoomId.ToString();
        int inRoom = 1 + LanPresence.GetPeers()
            .Count(p => string.Equals(p.Room, roomText, StringComparison.OrdinalIgnoreCase));

        if (inRoom >= 4)
        {
            MessageBox.Show(this, "В звонке уже 4 человека — больше не поместится.",
                "Звонок", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int invited = 0;
        foreach (var peer in LanPresence.GetPeers())
        {
            if (string.Equals(peer.Room, VoiceEngine.RoomId.ToString(), StringComparison.OrdinalIgnoreCase))
                continue;

            LanPresence.SendCallInvite(peer, VoiceEngine.RoomId);
            invited++;
        }

        MessageBox.Show(this,
            invited == 0 ? "В сети никого нет." : $"Приглашение отправлено: {invited}.",
            "Звонок", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void HangUp_Click(object sender, RoutedEventArgs e)
    {
        PttHook.Uninstall();
        VoiceEngine.LeaveCall();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => HangUp_Click(sender, e);
}
