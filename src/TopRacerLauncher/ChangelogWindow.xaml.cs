using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TopRacerLauncher;

/// <summary>Окно «Что нового»: история патчей списком, версии разворачиваются по клику.</summary>
public partial class ChangelogWindow : Window
{
    private static ChangelogWindow? _instance;

    public ChangelogWindow()
    {
        InitializeComponent();

        Opacity = 0;
        BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));

        CurrentVersionText.Text = "версия " + UpdateService.CurrentVersion;

        bool first = true;
        foreach (var entry in Changelog.Entries)
        {
            EntriesPanel.Children.Add(BuildEntry(entry, first));
            first = false;
        }
    }

    /// <summary>Открывает (или поднимает) окно истории версий.</summary>
    public static void Open(Window? owner = null)
    {
        if (_instance is { IsVisible: true })
        {
            _instance.Activate();
            return;
        }

        var window = new ChangelogWindow { Owner = owner };
        if (owner?.Topmost == true) window.Topmost = true;

        _instance = window;
        window.Closed += (_, _) => _instance = null;
        window.Show();
    }

    private Border BuildEntry(ChangelogEntry entry, bool expanded)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10),
        };
        card.SetResourceReference(Border.BackgroundProperty, "BgCardBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "GlassBorderBrush");

        var root = new StackPanel();

        // ---------- Шапка версии ----------
        var chevron = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 0,0 L 10,0 L 5,6 Z"),
            Width = 10,
            Height = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 4, 0),
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = new RotateTransform(expanded ? 0 : -90),
        };
        chevron.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "TextSecondaryBrush");

        var versionChip = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 3, 9, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
        };
        var versionText = new TextBlock
        {
            Text = entry.Version,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
        };
        versionText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
        versionChip.Child = versionText;

        var title = new TextBlock
        {
            Text = entry.Title,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

        var date = new TextBlock
        {
            Text = entry.Date,
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
        };
        date.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

        var headerGrid = new Grid { Margin = new Thickness(14, 12, 10, 12) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(versionChip, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(date, 2);
        Grid.SetColumn(chevron, 3);

        headerGrid.Children.Add(versionChip);
        headerGrid.Children.Add(title);
        headerGrid.Children.Add(date);
        headerGrid.Children.Add(chevron);

        var headerButton = new Border
        {
            Child = headerGrid,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };

        // ---------- Список изменений ----------
        var body = new StackPanel
        {
            Margin = new Thickness(16, 0, 16, 14),
            Visibility = expanded ? Visibility.Visible : Visibility.Collapsed,
        };

        foreach (var note in entry.Notes)
        {
            var line = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new Border
            {
                Width = 5,
                Height = 5,
                CornerRadius = new CornerRadius(3),
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 7, 10, 0),
            };
            dot.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

            var text = new TextBlock
            {
                Text = note,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");

            Grid.SetColumn(dot, 0);
            Grid.SetColumn(text, 1);
            line.Children.Add(dot);
            line.Children.Add(text);
            body.Children.Add(line);
        }

        headerButton.MouseLeftButtonUp += (_, _) => Toggle(body, chevron);

        root.Children.Add(headerButton);
        root.Children.Add(body);
        card.Child = root;

        return card;
    }

    /// <summary>Плавно разворачивает/сворачивает список изменений версии.</summary>
    private static void Toggle(StackPanel body, System.Windows.Shapes.Path chevron)
    {
        bool expand = body.Visibility != Visibility.Visible;

        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };

        if (expand)
        {
            body.Visibility = Visibility.Visible;

            // Сначала измеряем натуральную высоту, затем анимируем её от нуля.
            body.MaxHeight = double.PositiveInfinity;
            body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double target = body.DesiredSize.Height;
            if (double.IsNaN(target) || target < 1) target = 120;

            body.MaxHeight = target;
            body.Opacity = 1;

            body.BeginAnimation(MaxHeightProperty, new System.Windows.Media.Animation.DoubleAnimation(
                0, target, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = ease,
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
            });

            body.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(
                0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = ease,
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
            });
        }
        else
        {
            double from = Math.Max(body.ActualHeight, 1);

            body.MaxHeight = 0;
            var animation = new System.Windows.Media.Animation.DoubleAnimation(
                from, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = ease,
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
            };
            animation.Completed += (_, _) =>
            {
                body.Visibility = Visibility.Collapsed;
                body.MaxHeight = double.PositiveInfinity;
            };

            body.BeginAnimation(MaxHeightProperty, animation);
        }

        chevron.RenderTransform = new RotateTransform(expand ? 0 : -90);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
