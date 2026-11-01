using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TopRacerLauncher;

/// <summary>Чат с другом из локальной сети: пузыри сообщений, история, эмодзи.</summary>
public partial class ChatWindow : Window
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
        "\U0001F344", "\u2764", "\U0001F49A", "\U0001F4A5", "\U0001F3C5", "\U0001F9E0",
    };

    private readonly LanPeer _peer;

    public ChatWindow(LanPeer peer)
    {
        InitializeComponent();
        _peer = peer;

        HeaderText.Text = peer.Name;
        AvatarText.Text = Initials(peer.Name);
        SubText.Text = peer.Status;

        BuildEmojiPanel();
        BuildStickerPanel();

        if (Environment.GetCommandLineArgs().Any(a => a == "--demo-emoji"))
        {
            EmojiPanel.Visibility = Visibility.Visible;
            if (Environment.GetCommandLineArgs().Any(a => a == "--demo-stickers"))
            {
                EmojiGrid.Visibility = Visibility.Collapsed;
                StickerScroll.Visibility = Visibility.Visible;
            }
        }

        LanPresence.MessageReceived += OnMessage;
        Closed += (_, _) => LanPresence.MessageReceived -= OnMessage;

        LoadHistory();

        Loaded += (_, _) =>
        {
            MessageBox.Focus();
            LogScroll.ScrollToEnd();
        };
    }

    private static string Initials(string name)
    {
        var trimmed = (name ?? "").Trim();
        return trimmed.Length == 0 ? "?" : trimmed[..1].ToUpperInvariant();
    }

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

    private void BuildEmojiPanel()
    {
        foreach (var emoji in Emojis)
        {
            var button = new Button
            {
                Content = emoji,
                Height = 34,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = 17,
                Margin = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Вставить",
            };

            button.Click += (_, _) =>
            {
                MessageBox.Text += emoji;
                MessageBox.CaretIndex = MessageBox.Text.Length;
                MessageBox.Focus();
            };

            EmojiGrid.Children.Add(button);
        }
    }

    private void BuildStickerPanel()
    {
        foreach (var sticker in Stickers)
        {
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
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
                ToolTip = "Отправить стикер",
            };

            var code = sticker.Code;
            button.Click += (_, _) => SendSticker(code);

            StickerGrid.Children.Add(button);
        }
    }

    private void EmojiTab_Click(object sender, RoutedEventArgs e)
    {
        EmojiGrid.Visibility = Visibility.Visible;
        StickerScroll.Visibility = Visibility.Collapsed;
    }

    private void StickerTab_Click(object sender, RoutedEventArgs e)
    {
        EmojiGrid.Visibility = Visibility.Collapsed;
        StickerScroll.Visibility = Visibility.Visible;
    }

    private void SendSticker(string code)
    {
        var message = "::sticker:" + code + "::";

        LanPresence.SendMessage(_peer, message);
        ChatHistory.Append(_peer.Id, "Вы", message);

        AddBubble(message, true, DateTime.Now.ToString("HH:mm"));
    }

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

    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        EmojiPanel.Visibility = EmojiPanel.Visibility == Visibility.Visible
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

    private void OnMessage(LanPeer peer, string text)
    {
        if (!string.Equals(peer.Id, _peer.Id, StringComparison.Ordinal)) return;

        ChatHistory.Append(peer.Id, peer.Name, text);

        Dispatcher.BeginInvoke(new Action(() =>
        {
            AddBubble(text, false, DateTime.Now.ToString("HH:mm"));
            SubText.Text = peer.Status;
        }));
    }

    private void AddSystem(string text)
    {
        LogPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 8),
            Foreground = FindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray,
        });

        Dispatcher.BeginInvoke(new Action(() => LogScroll.ScrollToEnd()));
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

        var bubble = new Border
        {
            Child = content,
            MaxWidth = 320,
            Padding = new Thickness(12, 8, 12, 6),
            Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(14, 14, mine ? 4 : 14, mine ? 14 : 4),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = mine
                ? (FindResource("AccentBrush") as Brush ?? Brushes.SteelBlue)
                : new SolidColorBrush(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF)),
        };

        LogPanel.Children.Add(bubble);
        Dispatcher.BeginInvoke(new Action(() => LogScroll.ScrollToEnd()));
    }

    private void AddSticker((string Code, string Title, string Emoji, string Color) sticker, bool mine)
    {
        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

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

        LogPanel.Children.Add(new Border
        {
            Child = content,
            Width = 132,
            Height = 92,
            CornerRadius = new CornerRadius(16),
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(sticker.Color)),
        });

        Dispatcher.BeginInvoke(new Action(() => LogScroll.ScrollToEnd()));
    }

    private void Send_Click(object sender, RoutedEventArgs e) => Send();

    private void MessageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Send();
        }
    }

    private void Send()
    {
        var text = MessageBox.Text.Trim();
        if (text.Length == 0) return;

        LanPresence.SendMessage(_peer, text);
        ChatHistory.Append(_peer.Id, "Вы", text);

        AddBubble(text, true, DateTime.Now.ToString("HH:mm"));
        MessageBox.Clear();
        MessageBox.Focus();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
