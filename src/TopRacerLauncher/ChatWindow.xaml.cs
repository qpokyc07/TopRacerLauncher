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
    private static readonly string[] Emojis =
    {
        "\U0001F3AE", "\U0001F579", "\U0001F47E", "\U0001F47D", "\U0001F916", "\U0001F9DF",
        "\U0001F9D9", "\U0001F409", "\U0001F432", "\U0001F480", "\u2620", "\U0001F3C6",
        "\U0001F947", "\U0001F948", "\U0001F949", "\U0001F396", "\U0001F3AF", "\U0001F52B",
        "\U0001F5E1", "\u2694", "\U0001F6E1", "\U0001F3F9", "\U0001F4A3", "\U0001F9E8",
        "\U0001F680", "\U0001F4A5", "\U0001F525", "\u2B50", "\U0001F31F", "\u2728",
        "\U0001F48E", "\U0001FA99", "\U0001F381", "\U0001F0CF", "\U0001F3B2", "\U0001F3B0",
        "\U0001F3C1", "\U0001F697", "\U0001F3CE", "\U0001F699", "\U0001F3CD", "\U0001F681",
        "\u2708", "\u26F5", "\U0001F3F0", "\U0001F5FA", "\U0001F9ED", "\u23F1",
        "\u26A1", "\U0001F9CA", "\U0001F32A", "\U0001F451", "\U0001F9B8", "\U0001F9B9",
        "\U0001F344", "\u2764", "\U0001F49A", "\U0001F3C5", "\U0001F9E0",
    };

    private static readonly (string Code, string Title, string Emoji, string Color)[] Stickers =
    {
        ("cs", "GO CS", "\U0001F3AF", "#E8542F"),
        ("roblox", "GO ROBLOX", "\U0001F7E5", "#C0392B"),
        ("mine", "GO MINE", "\u26CF", "#2E7D32"),
        ("dota", "GO DOTA", "\u2694", "#B71C1C"),
        ("fortnite", "GO FORTNITE", "\U0001F3D7", "#1565C0"),
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
    private readonly Border _emojiPanel;

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

        var emojiButton = new Button
        {
            Content = "\u263A",
            FontFamily = new FontFamily("Segoe UI Symbol"),
            FontSize = 18,
            Width = 40,
            Height = 40,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0),
            Style = Resource<Style>("BaseButton"),
            ToolTip = "Эмодзи и стикеры",
        };
        emojiButton.Click += (_, _) => ToggleEmoji();

        var inputRow = new Grid { Margin = new Thickness(16, 10, 16, 14) };
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(emojiButton, 0);
        Grid.SetColumn(_input, 1);
        Grid.SetColumn(sendButton, 2);
        inputRow.Children.Add(emojiButton);
        inputRow.Children.Add(_input);
        inputRow.Children.Add(sendButton);

        _emojiPanel = BuildEmojiPanel();
        _emojiPanel.Visibility = Visibility.Collapsed;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_scroll, 0);
        Grid.SetRow(_emojiPanel, 1);
        Grid.SetRow(inputRow, 2);
        root.Children.Add(_scroll);
        root.Children.Add(_emojiPanel);
        root.Children.Add(inputRow);

        Content = root;

        LanPresence.MessageReceived += OnPresenceMessage;
        LoadHistory();
    }

    public void Detach() => LanPresence.MessageReceived -= OnPresenceMessage;

    public void FocusInput() => _input.Focus();

    private static T? Resource<T>(string key) where T : class
        => Application.Current?.TryFindResource(key) as T;

    private Border BuildEmojiPanel()
    {
        var emojiGrid = new UniformGrid { Columns = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var emoji in Emojis)
        {
            var button = new Button
            {
                Content = emoji,
                Height = 34,
                FontSize = 17,
                Margin = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            button.Click += (_, _) =>
            {
                _input.Text += emoji;
                _input.CaretIndex = _input.Text.Length;
                _input.Focus();
            };
            emojiGrid.Children.Add(button);
        }

        var emojiScroll = new ScrollViewer
        {
            Content = emojiGrid,
            MaxHeight = 140,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var stickerGrid = new UniformGrid { Columns = 4, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var sticker in Stickers)
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = sticker.Emoji,
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
            });
            content.Children.Add(new TextBlock
            {
                Text = sticker.Title,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
            });

            var button = new Button
            {
                Content = content,
                Height = 48,
                Margin = new Thickness(3),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sticker.Color)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
            };
            var code = sticker.Code;
            button.Click += (_, _) => SendSticker(code);
            stickerGrid.Children.Add(button);
        }

        var stickerScroll = new ScrollViewer
        {
            Content = stickerGrid,
            MaxHeight = 140,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed,
        };

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

        var emojiTab = new Button
        {
            Content = "Эмодзи",
            Height = 26,
            Padding = new Thickness(12, 0, 12, 0),
            FontSize = 11.5,
            Style = Resource<Style>("BaseButton"),
        };
        var stickerTab = new Button
        {
            Content = "Стикеры",
            Height = 26,
            Padding = new Thickness(12, 0, 12, 0),
            FontSize = 11.5,
            Margin = new Thickness(6, 0, 0, 0),
            Style = Resource<Style>("BaseButton"),
        };

        emojiTab.Click += (_, _) =>
        {
            emojiScroll.Visibility = Visibility.Visible;
            stickerScroll.Visibility = Visibility.Collapsed;
        };
        stickerTab.Click += (_, _) =>
        {
            emojiScroll.Visibility = Visibility.Collapsed;
            stickerScroll.Visibility = Visibility.Visible;
        };

        tabs.Children.Add(emojiTab);
        tabs.Children.Add(stickerTab);

        var stack = new StackPanel();
        stack.Children.Add(tabs);
        stack.Children.Add(emojiScroll);
        stack.Children.Add(stickerScroll);

        return new Border
        {
            Child = stack,
            Background = Resource<Brush>("BgPanelBrush"),
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
    }

    private void ToggleEmoji()
    {
        _emojiPanel.Visibility = _emojiPanel.Visibility == Visibility.Visible
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
            FontSize = 30,
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
            Width = 132,
            Height = 92,
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sticker.Color)),
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
