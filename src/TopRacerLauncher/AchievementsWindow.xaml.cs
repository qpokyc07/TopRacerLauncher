using System.Windows;
using System.Windows.Media;

namespace TopRacerLauncher;

/// <summary>Карточка достижения на полке.</summary>
public sealed class AchievementVm
{
    public string Icon { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string ProgressText { get; init; } = "";
    public string DateText { get; init; } = "";
    public double Percent { get; init; }
    public bool IsUnlocked { get; init; }
    public Brush CardBorder { get; init; } = Brushes.Transparent;

    /// <summary>Закрытые достижения приглушены, чтобы открытые читались сразу.</summary>
    public double Opacity => IsUnlocked ? 1.0 : 0.55;

    public Visibility DateVisibility => IsUnlocked ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Закрытое достижение помечаем замком — видно, что ещё впереди.</summary>
    public Visibility LockVisibility => IsUnlocked ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>
/// Полка достижений: прогресс берётся из накопленной статистики, поэтому
/// окно всегда показывает актуальное состояние — пересчёт по кнопке не нужен.
/// </summary>
public partial class AchievementsWindow : Window
{
    public AchievementsWindow()
    {
        InitializeComponent();

        // Окно ничего не «выдаёт» само — только догоняет то, что уже заслужено
        // (например, после игры, закрытой вместе с лаунчером). Тосты при этом молчат.
        try { Achievements.Evaluate(notify: false); } catch { /* не критично */ }

        Build();

        // Пока окно открыто, новое достижение сразу появится на полке.
        Achievements.Unlocked += OnUnlocked;
        Closed += (_, _) => Achievements.Unlocked -= OnUnlocked;
    }

    private void OnUnlocked(List<Achievement> _) => Build();

    private void Build()
    {
        var snapshot = Achievements.BuildSnapshot();
        var items = new List<AchievementVm>();

        foreach (var achievement in Achievements.Catalog)
        {
            int value;
            try { value = Math.Min(achievement.Measure(snapshot), achievement.Target); }
            catch { value = 0; }

            var unlockedAt = Achievements.UnlockedAt(achievement.Id);

            items.Add(new AchievementVm
            {
                Icon = achievement.Icon,
                Title = achievement.Title,
                Description = achievement.Description,
                ProgressText = unlockedAt.HasValue
                    ? achievement.ProgressText(achievement.Target) + "  •  получено"
                    : achievement.ProgressText(value),
                DateText = unlockedAt.HasValue
                    ? unlockedAt.Value.ToLocalTime().ToString("dd.MM.yyyy")
                    : "",
                Percent = achievement.Target <= 0 ? 100 : 100.0 * value / achievement.Target,
                IsUnlocked = unlockedAt.HasValue,
                CardBorder = unlockedAt.HasValue ? Accent() : Border(),
            });
        }

        // Открытые — вперёд, дальше по близости к цели: так полка мотивирует.
        ItemsHost.ItemsSource = items
            .OrderByDescending(v => v.IsUnlocked)
            .ThenByDescending(v => v.IsUnlocked ? 0 : v.Percent)
            .ToList();

        int unlocked = Achievements.UnlockedCount;
        int total = Achievements.TotalCount;

        CounterText.Text = $"{unlocked} из {total}";
        HoursText.Text = snapshot.HoursText;
        LibraryText.Text = snapshot.LibraryCount.ToString();
        StreakText.Text = snapshot.MaxStreakDays + " " + PluralDays(snapshot.MaxStreakDays);
        PercentText.Text = (total == 0 ? 0 : (int)Math.Round(100.0 * unlocked / total)) + "%";
        TotalProgress.Value = total == 0 ? 0 : 100.0 * unlocked / total;
    }

    private static string PluralDays(int n)
    {
        int mod100 = n % 100;
        int mod10 = n % 10;

        if (mod100 is >= 11 and <= 14) return "дней";
        return mod10 switch { 1 => "день", 2 or 3 or 4 => "дня", _ => "дней" };
    }

    private static Brush Accent() =>
        Application.Current?.TryFindResource("AccentBrush") as Brush ?? Brushes.Transparent;

    private static Brush Border() =>
        Application.Current?.TryFindResource("BorderBrush") as Brush ?? Brushes.Transparent;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
