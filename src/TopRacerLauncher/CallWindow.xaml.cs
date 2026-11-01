using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        var list = new List<CallParticipant>
        {
            new()
            {
                Id = LanPresence.SelfGuid.ToString(),
                Name = LanPresence.SelfName + " (вы)",
                Status = "микрофон " + VoiceEngine.InputGain + "%",
                IsMe = true,
            },
        };

        list.AddRange(LanPresence.GetPeers()
            .Where(p => string.Equals(p.Room, VoiceEngine.RoomId.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(p => new CallParticipant
            {
                Id = p.Id,
                Name = p.Name,
                Status = "громкость " + VoiceEngine.GetPeerVolume(Guid.TryParse(p.Id, out var gid) ? gid : Guid.Empty) + "%",
            }));

        ParticipantsList.ItemsSource = list;
        CountText.Text = $"{list.Count}/4";
    }

    /// <summary>ПКМ по участнику — ползунок его громкости (или своего микрофона).</summary>
    private void Participant_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not CallParticipant participant) return;
        e.Handled = true;

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = fe };

        var panel = new StackPanel { Margin = new Thickness(12), Width = 190 };
        panel.Children.Add(new TextBlock
        {
            Text = participant.IsMe ? "Громкость микрофона" : "Громкость: " + participant.Name,
            FontWeight = FontWeights.Bold,
            FontSize = 12,
        });

        int current = participant.IsMe
            ? VoiceEngine.InputGain
            : VoiceEngine.GetPeerVolume(Guid.TryParse(participant.Id, out var id) ? id : Guid.Empty);

        var valueText = new TextBlock
        {
            Text = current + "%",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 200,
            Value = current,
            TickFrequency = 10,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 8, 0, 0),
        };

        slider.ValueChanged += (_, _) =>
        {
            int value = (int)slider.Value;
            valueText.Text = value + "%";

            if (participant.IsMe)
            {
                VoiceEngine.InputGain = value;
                App.Settings.VoiceInputGain = value;
            }
            else if (Guid.TryParse(participant.Id, out var peerId))
            {
                VoiceEngine.SetPeerVolume(peerId, value);
            }

            App.SaveState();
            RefreshParticipants();
        };

        panel.Children.Add(slider);
        panel.Children.Add(valueText);
        menu.Items.Add(panel);
        menu.IsOpen = true;
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

/// <summary>Строка участника звонка.</summary>
public sealed class CallParticipant
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";
    public bool IsMe { get; init; }
}