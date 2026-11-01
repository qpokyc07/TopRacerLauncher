using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace TopRacerLauncher;

/// <summary>Окно чатов: один собеседник — одна вкладка.</summary>
public partial class ChatWindow : Window
{
    private static ChatWindow? _instance;

    private readonly Dictionary<string, ChatTab> _tabs = new();

    public ChatWindow()
    {
        InitializeComponent();

        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

        Closed += (_, _) =>
        {
            foreach (var tab in _tabs.Values) tab.Detach();
            if (ReferenceEquals(_instance, this)) _instance = null;
        };
    }

    /// <summary>Открывает (или активирует) вкладку чата с другом.</summary>
    public static void Open(LanPeer peer, Window? owner = null)
    {
        if (_instance == null)
        {
            _instance = new ChatWindow { Owner = owner };
            _instance.Show();
        }

        _instance.ShowTab(peer);
        _instance.Activate();
    }

    /// <summary>Открыт ли чат с этим другом прямо сейчас.</summary>
    public static bool IsOpenWith(string peerId)
        => _instance is { IsVisible: true } window && window._tabs.ContainsKey(peerId);

    /// <summary>Входящее сообщение: доставляем во вкладку этого собеседника.</summary>
    public static void NotifyIncoming(LanPeer peer, string text)
    {
        if (_instance == null || !_instance.IsVisible) return;
        if (_instance._tabs.TryGetValue(peer.Id, out var tab)) tab.ReceiveIncoming(text);
    }

    private void ShowTab(LanPeer peer)
    {
        if (_tabs.TryGetValue(peer.Id, out _))
        {
            ActivateTab(peer.Id);
            return;
        }

        var tab = new ChatTab(peer);
        _tabs[peer.Id] = tab;

        var button = new Button
        {
            Content = peer.Name,
            Tag = peer.Id,
            Height = 30,
            Padding = new Thickness(14, 0, 14, 0),
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 6, 0),
            Style = FindResource("BaseButton") as Style,
            Cursor = Cursors.Hand,
        };
        button.Click += (_, _) => ActivateTab((string)button.Tag);

        TabsPanel.Children.Add(button);
        ActivateTab(peer.Id);
    }

    private void ActivateTab(string peerId)
    {
        if (!_tabs.TryGetValue(peerId, out var tab)) return;

        TabHost.Content = tab.Content;

        foreach (var child in TabsPanel.Children)
        {
            if (child is not Button button || button.Tag is not string id) continue;

            bool active = id == peerId;
            button.Background = active
                ? FindResource("AccentBrush") as Brush ?? Brushes.SteelBlue
                : FindResource("BgCardBrush") as Brush ?? Brushes.DimGray;
            button.Foreground = active ? Brushes.White : FindResource("TextBrush") as Brush ?? Brushes.White;
        }

        tab.FocusInput();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Одна вкладка чата: лог, панель эмодзи/стикеров и поле ввода.</summary>
public sealed class ChatTab
{
    private static readonly (string Code, string Title, string Emoji, string Color)[] Stickers =
    {
        ("cs", "GO CS", "\U0001F3AF", "#E8542F"),
        ("roblox", "GO ROBLOX", "\U0001F9F1", "#C0392B"),
        ("mine", "GO MINE", "\u26CF", "#2E7D32"),
        ("dota", "GO DOTA", "\u2694", "#B71C1C"),
        ("fortnite", "GO FORTNITE", "\U0001F528", "#1565C0"),
        ("gta", "GO GTA", "\U0001F697", "#F9A825"),
        ("among", "GO AMONG US", "\U0001F52A", "#6A1B9A"),
        ("gg", "GG WP", "\U0001F3C6", "#00838F"),
        ("noob", "NOOB", "\U0001F95A", "#6D4C41"),
        ("v1", "1V1 ME", "\U0001F94A", "#D84315"),
        ("rip", "RIP", "\U0001F480", "#37474F"),
        ("win", "VICTORY", "\U0001F451", "#FFB300"),
    };

    private readonly LanPeer _peer;
    private readonly StackPanel _log = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input;
    private readonly Border _stickerPanel;

    public FrameworkElement Content { get; }

    public ChatTab(LanPeer peer)
    {
        _peer = peer;

        _log.Margin = new Thickness(16, 12, 16, 0);

        _scroll = new ScrollViewer
        {
            Content = _log,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        _input = new TextBox { Style = Resource<Style>("BaseTextBox"), Height = 40 };
        _input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Send();
        };

        var sendButton = new Button
        {
            Content = "Отправить",
            Height = 40,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(8, 0, 0, 0),
            Style = Resource<Style>("PrimaryButton"),
        };
        sendButton.Click += (_, _) => Send();

        var stickerButton = new Button
        {
            Content = "Стикеры",
            Height = 40,
            Padding = new Thickness(14, 0, 14, 0),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
            Style = Resource<Style>("BaseButton"),
            ToolTip = "Открыть стикеры",
        };
        stickerButton.Click += (_, _) => ToggleStickers();
        var inputRow = new Grid { Margin = new Thickness(16, 10, 16, 14) };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(stickerButton, 0);
        Grid.SetColumn(_input, 1);
        Grid.SetColumn(sendButton, 2);
        inputRow.Children.Add(stickerButton);
        inputRow.Children.Add(_input);
        inputRow.Children.Add(sendButton);

        _stickerPanel = BuildStickerPanel();
        _stickerPanel.Visibility = Environment.GetCommandLineArgs().Any(a => a == "--demo-emoji")
            ? Visibility.Visible
            : Visibility.Collapsed;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_scroll, 0);
        Grid.SetRow(_stickerPanel, 1);
        Grid.SetRow(inputRow, 2);
        root.Children.Add(_scroll);
        root.Children.Add(_stickerPanel);
        root.Children.Add(inputRow);

        Content = root;

        LanPresence.MessageReceived += OnPresenceMessage;
        LoadHistory();
    }

    public void Detach() => LanPresence.MessageReceived -= OnPresenceMessage;

    public void FocusInput() => _input.Focus();

    private static T? Resource<T>(string key) where T : class
        => Application.Current?.TryFindResource(key) as T;

    private Border BuildStickerPanel()
    {
        var grid = new UniformGrid { Columns = 4, HorizontalAlignment = HorizontalAlignment.Stretch };

        foreach (var sticker in Stickers)
        {
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(new TextBlock
            {
                Text = sticker.Emoji,
                FontSize = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            content.Children.Add(new TextBlock
            {
                Text = sticker.Title,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
            });

            // «Наклейка»: цветной фон + белый контур + мягкая тень.
            var stickerCard = new Border
            {
                Child = content,
                Height = 52,
                Margin = new Thickness(5),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sticker.Color)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2.5),
                Cursor = Cursors.Hand,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 10,
                    ShadowDepth = 2,
                    Opacity = 0.45,
                },
            };

            var code = sticker.Code;
            stickerCard.MouseLeftButtonUp += (_, _) => SendSticker(code);
            grid.Children.Add(stickerCard);
        }

        var scroll = new ScrollViewer
        {
            Content = grid,
            MaxHeight = 150,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        return new Border
        {
            Child = scroll,
            Background = Resource<Brush>("BgPanelBrush"),
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
    }
    private void ToggleStickers()
    {
        _stickerPanel.Visibility = _stickerPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void LoadHistory()
    {
        var history = ChatHistory.Load(_peer.Id);
        if (history.Count == 0)
        {
            AddSystem("Вы переписываетесь с " + _peer.Name + ".");
            return;
        }

        var previousDay = DateTime.MinValue;
        foreach (var entry in history)
        {
            var local = entry.Ts.ToLocalTime();

            if (local.Date != previousDay)
            {
                previousDay = local.Date;
                AddSystem(local.Date == DateTime.Today ? "Сегодня" : local.ToString("dd.MM.yyyy"));
            }

            AddBubble(entry.Text, entry.From == "Вы", local.ToString("HH:mm"));
        }
    }

    private void OnPresenceMessage(LanPeer peer, string text)
    {
        if (!string.Equals(peer.Id, _peer.Id, StringComparison.Ordinal)) return;

        ChatHistory.Append(peer.Id, peer.Name, text);

        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsActive()) AddSystem("Новое сообщение…");
            AddBubble(text, false, DateTime.Now.ToString("HH:mm"));
        }));
    }

    /// <summary>Показывает входящее сообщение (вызывается и для неактивной вкладки).</summary>
    public void ReceiveIncoming(string text)
    {
        Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsActive()) AddSystem("Новое сообщение…");
            AddBubble(text, false, DateTime.Now.ToString("HH:mm"));
        }));
    }

    private bool IsActive() => Content.IsVisible;

    private void AddSystem(string text)
    {
        _log.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 8),
            Foreground = Resource<Brush>("TextSecondaryBrush") ?? Brushes.Gray,
        });
        ScrollDown();
    }

    private void AddBubble(string text, bool mine, string time)
    {
        if (TryGetSticker(text, out var sticker))
        {
            AddSticker(sticker, mine);
            return;
        }

        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
        });
        content.Children.Add(new TextBlock
        {
            Text = time,
            FontSize = 9.5,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
        });

        _log.Children.Add(new Border
        {
            Child = content,
            MaxWidth = 330,
            Padding = new Thickness(12, 8, 12, 6),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(14, 14, mine ? 4 : 14, mine ? 14 : 4),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = mine
                ? Resource<Brush>("AccentBrush") ?? Brushes.SteelBlue
                : new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
        });

        ScrollDown();
    }

    private void AddSticker((string Code, string Title, string Emoji, string Color) sticker, bool mine)
    {
        var content = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock
        {
            Text = sticker.Emoji,
            FontSize = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        content.Children.Add(new TextBlock
        {
            Text = sticker.Title,
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 0),
        });

        _log.Children.Add(new Border
        {
            Child = content,
            Width = 140,
            Height = 98,
            CornerRadius = new CornerRadius(18),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sticker.Color)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(3),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.5,
            },
        });

        ScrollDown();
    }

    private void ScrollDown()
        => Application.Current?.Dispatcher.BeginInvoke(new Action(() => _scroll.ScrollToEnd()));

    private static bool TryGetSticker(string text, out (string Code, string Title, string Emoji, string Color) sticker)
    {
        sticker = default;

        if (!text.StartsWith("::sticker:", StringComparison.Ordinal) || !text.EndsWith("::", StringComparison.Ordinal))
            return false;

        var code = text["::sticker:".Length..^2];
        foreach (var item in Stickers)
        {
            if (item.Code == code) { sticker = item; return true; }
        }

        return false;
    }

    private void Send()
    {
        var text = _input.Text.Trim();
        if (text.Length == 0) return;

        LanPresence.SendMessage(_peer, text);
        ChatHistory.Append(_peer.Id, "Вы", text);

        AddBubble(text, true, DateTime.Now.ToString("HH:mm"));
        _input.Clear();
        _input.Focus();
    }

    private void SendSticker(string code)
    {
        var message = "::sticker:" + code + "::";

        LanPresence.SendMessage(_peer, message);
        ChatHistory.Append(_peer.Id, "Вы", message);

        AddBubble(message, true, DateTime.Now.ToString("HH:mm"));
    }
}
