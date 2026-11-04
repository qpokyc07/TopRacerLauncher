using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>
/// Оверлей звонка поверх игры (как в Discord): список участников, ник говорящего подсвечивается.
/// Окно не забирает фокус и пропускает клики сквозь себя.
/// </summary>
public partial class VoiceOverlayWindow : Window
{
    private static VoiceOverlayWindow? _instance;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<Row> _rows = new();
    private string _signature = "";

    public VoiceOverlayWindow()
    {
        InitializeComponent();

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            PlaceInCorner();
            Refresh();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>Показать/обновить или спрятать оверлей (вызывается при изменении состояния звонка).</summary>
    public static void Update()
    {
        bool wanted = App.Settings.VoiceOverlay && VoiceEngine.InCall;

        if (!wanted)
        {
            if (_instance != null)
            {
                _instance.Close();
                _instance = null;
            }
            return;
        }

        if (_instance == null)
        {
            _instance = new VoiceOverlayWindow();
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }

        _instance.Refresh();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Клики проходят сквозь оверлей, окно не активируется и не мешает Alt+Tab.
        var handle = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExNoActivate | WsExToolWindow);
    }

    private void PlaceInCorner()
    {
        Left = 20;
        Top = SystemParameters.WorkArea.Top + 20;
    }

    /// <summary>Обновляет список участников и подсветку говорящего.</summary>
    private void Refresh()
    {
        if (!VoiceEngine.InCall) return;

        var participants = new List<(string Id, string Name, bool IsMe)>();

        if (!string.IsNullOrWhiteSpace(LanPresence.SelfName))
            participants.Add((LanPresence.SelfGuid.ToString(), LanPresence.SelfName + " (вы)", true));

        string room = VoiceEngine.RoomId.ToString();
        foreach (var peer in LanPresence.GetPeers())
        {
            if (!string.Equals(peer.Room, room, StringComparison.OrdinalIgnoreCase)) continue;
            participants.Add((peer.Id, peer.Name, false));
        }

        string signature = string.Join("|", participants.Select(p => p.Id + ":" + p.Name));
        if (signature != _signature)
        {
            _signature = signature;
            RebuildRows(participants);
        }

        CountText.Text = participants.Count + "/4";

        // Подсветка: у себя — передача идёт, у остальных — недавно слышали пакеты.
        foreach (var row in _rows)
        {
            bool speaking = row.IsMe
                ? VoiceEngine.IsTransmitting
                : Guid.TryParse(row.Id, out var id) && VoiceEngine.IsPeerSpeaking(id);

            row.SetSpeaking(speaking);
        }
    }

    private void RebuildRows(List<(string Id, string Name, bool IsMe)> participants)
    {
        RowsPanel.Children.Clear();
        _rows.Clear();

        foreach (var (id, name, isMe) in participants)
        {
            var row = new Row(id, name, isMe);
            _rows.Add(row);
            RowsPanel.Children.Add(row.Element);
        }
    }

    /// <summary>Строка участника: аватар с буквой, ник и точка «говорит».</summary>
    private sealed class Row
    {
        private readonly Border _avatar;
        private readonly TextBlock _initial;
        private readonly TextBlock _name;
        private readonly Border _marker;

        public string Id { get; }
        public bool IsMe { get; }
        public FrameworkElement Element { get; }

        public Row(string id, string name, bool isMe)
        {
            Id = id;
            IsMe = isMe;

            _initial = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim().Substring(0, 1).ToUpperInvariant(),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _avatar = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromRgb(0x35, 0x37, 0x45)),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _initial,
            };

            _name = new TextBlock
            {
                Text = name,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0, 6, 0),
            };

            _marker = new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(_avatar, 0);
            Grid.SetColumn(_name, 1);
            Grid.SetColumn(_marker, 2);
            grid.Children.Add(_avatar);
            grid.Children.Add(_name);
            grid.Children.Add(_marker);

            Element = grid;
            SetSpeaking(false);
        }

        public void SetSpeaking(bool speaking)
        {
            var accent = Application.Current?.TryFindResource("AccentBrush") as Brush
                         ?? new SolidColorBrush(Color.FromRgb(0x7C, 0x5C, 0xFF));

            _avatar.Background = speaking
                ? accent
                : new SolidColorBrush(Color.FromRgb(0x35, 0x37, 0x45));

            _name.Foreground = speaking
                ? Brushes.White
                : new SolidColorBrush(Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF));

            _marker.Background = speaking ? accent : Brushes.Transparent;
        }
    }

    // ---- Win32: клики сквозь окно ----

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
