using System.IO;
using System.Text;
using System.Text.Json;

namespace TopRacerLauncher;

/// <summary>
/// Достижение в каталоге. Само по себе ничего не «выдаёт»: значение прогресса
/// считается из уже накопленных данных (время игр и журнал событий), поэтому
/// достижения не теряются при обновлении и не требуют отдельного учёта.
/// </summary>
public sealed class Achievement
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";

    /// <summary>Иконка (эмодзи).</summary>
    public string Icon { get; init; } = "\U0001F3C6";

    /// <summary>Сколько нужно набрать, чтобы достижение открылось.</summary>
    public int Target { get; init; } = 1;

    /// <summary>Единица прогресса: «ч», «шт», «дн» или пусто.</summary>
    public string Unit { get; init; } = "";

    /// <summary>Текущий прогресс по снимку статистики.</summary>
    public Func<AchievementSnapshot, int> Measure { get; init; } = _ => 0;

    /// <summary>Текст прогресса вида «37 / 100 ч».</summary>
    public string ProgressText(int value)
    {
        var val = Math.Min(value, Target);
        return string.IsNullOrEmpty(Unit)
            ? $"{val} / {Target}"
            : $"{val} / {Target} {Unit}";
    }
}

/// <summary>Открытое достижение: когда получено.</summary>
public sealed class UnlockedAchievement
{
    public string Id { get; set; } = "";
    public DateTime AtUtc { get; set; }
}

/// <summary>Файл достижений (achievements.json).</summary>
public sealed class AchievementState
{
    public int Version { get; set; } = 1;
    public List<UnlockedAchievement> Unlocked { get; set; } = new();
}

/// <summary>Готовый снимок статистики — считается один раз на проверку.</summary>
public sealed class AchievementSnapshot
{
    public int TotalSeconds { get; init; }
    public int TotalLaunches { get; init; }
    public int DistinctGames { get; init; }
    public int LibraryCount { get; init; }
    public int LongestSessionSeconds { get; init; }
    public int MaxGamesInDay { get; init; }
    public int MaxStreakDays { get; init; }
    public bool EarlyBird { get; init; }
    public int CleanDays { get; init; }

    public string HoursText
    {
        get
        {
            var t = TimeSpan.FromSeconds(TotalSeconds);
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours} ч {t.Minutes:D2} мин" : $"{t.Minutes} мин";
        }
    }
}

/// <summary>
/// Достижения игрока. Всё хранится локально в %APPDATA%\TopRacerLauncher\achievements.json,
/// а прогресс вычисляется из playtime.jsonl и history.jsonl — отдельных «счётчиков»
/// заводить не нужно, поэтому старые сессии тоже засчитываются.
/// </summary>
public static class Achievements
{
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Открылось новое достижение (уже в потоке UI, если приложение запущено).</summary>
    public static event Action<List<Achievement>>? Unlocked;

    public static string FilePath => Path.Combine(ConfigStore.ConfigDirectory, "achievements.json");

    private static AchievementState? _state;

    /// <summary>Каталог достижений. Порядок — от простых к сложным.</summary>
    public static List<Achievement> Catalog { get; } = new()
    {
        new Achievement
        {
            Id = "first_game", Title = "Первый шаг", Icon = "\U0001F3AE",
            Description = "Запустить первую игру из лаунчера",
            Target = 1, Measure = s => s.TotalLaunches > 0 ? 1 : 0,
        },
        new Achievement
        {
            Id = "hour_1", Title = "Первый час", Icon = "\u23F1",
            Description = "Наиграть один час",
            Target = 1, Unit = "ч", Measure = s => s.TotalSeconds / 3600,
        },
        new Achievement
        {
            Id = "launch_10", Title = "Разогрев", Icon = "\U0001F525",
            Description = "Запустить игры 10 раз",
            Target = 10, Unit = "шт", Measure = s => s.TotalLaunches,
        },
        new Achievement
        {
            Id = "games_5", Title = "Пять разных", Icon = "\U0001F5C2",
            Description = "Поиграть в пять разных игр",
            Target = 5, Unit = "шт", Measure = s => s.DistinctGames,
        },
        new Achievement
        {
            Id = "hour_10", Title = "Десятка", Icon = "\U0001F55B",
            Description = "Наиграть десять часов",
            Target = 10, Unit = "ч", Measure = s => s.TotalSeconds / 3600,
        },
        new Achievement
        {
            Id = "streak_3", Title = "Три дня подряд", Icon = "\U0001F4C5",
            Description = "Играть три дня подряд",
            Target = 3, Unit = "дн", Measure = s => s.MaxStreakDays,
        },
        new Achievement
        {
            Id = "early_bird", Title = "Жаворонок", Icon = "\U0001F305",
            Description = "Запустить игру раньше восьми утра",
            Target = 1, Measure = s => s.EarlyBird ? 1 : 0,
        },
        new Achievement
        {
            Id = "variety", Title = "Дегустация", Icon = "\U0001F37D",
            Description = "Поиграть в три разные игры за один день",
            Target = 3, Unit = "шт", Measure = s => s.MaxGamesInDay,
        },
        new Achievement
        {
            Id = "lib_10", Title = "Полка", Icon = "\U0001F4DA",
            Description = "Собрать в библиотеке десять игр",
            Target = 10, Unit = "шт", Measure = s => s.LibraryCount,
        },
        new Achievement
        {
            Id = "marathon", Title = "Марафон", Icon = "\U0001F3C1",
            Description = "Провести в игре два часа без перерыва",
            Target = 1, Measure = s => s.LongestSessionSeconds >= 7200 ? 1 : 0,
        },
        new Achievement
        {
            Id = "games_10", Title = "Коллекционер", Icon = "\U0001F4E6",
            Description = "Поиграть в десять разных игр",
            Target = 10, Unit = "шт", Measure = s => s.DistinctGames,
        },
        new Achievement
        {
            Id = "discipline", Title = "Дисциплина", Icon = "\U0001F6E1",
            Description = "Неделя без попыток подобрать пароль",
            Target = 7, Unit = "дн", Measure = s => s.CleanDays,
        },
        new Achievement
        {
            Id = "launch_100", Title = "Сотня стартов", Icon = "\U0001F680",
            Description = "Запустить игры 100 раз",
            Target = 100, Unit = "шт", Measure = s => s.TotalLaunches,
        },
        new Achievement
        {
            Id = "hour_50", Title = "Полсотни", Icon = "\u231B",
            Description = "Наиграть пятьдесят часов",
            Target = 50, Unit = "ч", Measure = s => s.TotalSeconds / 3600,
        },
        new Achievement
        {
            Id = "streak_7", Title = "Неделя в игре", Icon = "\U0001F5D3",
            Description = "Играть семь дней подряд",
            Target = 7, Unit = "дн", Measure = s => s.MaxStreakDays,
        },
        new Achievement
        {
            Id = "lib_25", Title = "Большая полка", Icon = "\U0001F3DB",
            Description = "Собрать в библиотеке двадцать пять игр",
            Target = 25, Unit = "шт", Measure = s => s.LibraryCount,
        },
        new Achievement
        {
            Id = "games_25", Title = "Библиотекарь", Icon = "\U0001F9ED",
            Description = "Поиграть в двадцать пять разных игр",
            Target = 25, Unit = "шт", Measure = s => s.DistinctGames,
        },
        new Achievement
        {
            Id = "hour_100", Title = "Сотня часов", Icon = "\U0001F31F",
            Description = "Наиграть сто часов",
            Target = 100, Unit = "ч", Measure = s => s.TotalSeconds / 3600,
        },
        new Achievement
        {
            Id = "streak_30", Title = "Месяц с лаунчером", Icon = "\U0001F3C6",
            Description = "Играть тридцать дней подряд",
            Target = 30, Unit = "дн", Measure = s => s.MaxStreakDays,
        },
        new Achievement
        {
            Id = "hour_500", Title = "Легенда", Icon = "\U0001F451",
            Description = "Наиграть пятьсот часов",
            Target = 500, Unit = "ч", Measure = s => s.TotalSeconds / 3600,
        },
    };

    /// <summary>Сколько достижений уже открыто.</summary>
    public static int UnlockedCount
    {
        get
        {
            lock (Gate)
            {
                EnsureLoaded();
                return _state!.Unlocked.Count;
            }
        }
    }

    public static int TotalCount => Catalog.Count;

    /// <summary>Открыто ли достижение (и когда).</summary>
    public static DateTime? UnlockedAt(string id)
    {
        lock (Gate)
        {
            EnsureLoaded();
            var item = _state!.Unlocked.FirstOrDefault(u => u.Id == id);
            return item?.AtUtc;
        }
    }

    /// <summary>Читает файл достижений (лениво, при первом обращении).</summary>
    public static void Load()
    {
        lock (Gate)
        {
            _state = null;
            EnsureLoaded();
        }
    }

    private static void EnsureLoaded()
    {
        if (_state != null) return;

        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath, Encoding.UTF8);
                var loaded = string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<AchievementState>(json, JsonOptions);

                if (loaded != null)
                {
                    loaded.Unlocked ??= new List<UnlockedAchievement>();
                    // Битая или несуществующая запись не должна ломать окно достижений.
                    loaded.Unlocked = loaded.Unlocked
                        .Where(u => u != null && !string.IsNullOrWhiteSpace(u.Id))
                        .GroupBy(u => u.Id)
                        .Select(g => g.First())
                        .ToList();

                    _state = loaded;
                    return;
                }
            }
        }
        catch { /* битый файл — начинаем с чистого списка, но не удаляем его */ }

        _state = new AchievementState();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigStore.ConfigDirectory);
            var json = JsonSerializer.Serialize(_state, JsonOptions);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* достижения не критичны для работы лаунчера */ }
    }

    /// <summary>
    /// Пересчитывает прогресс и записывает новые достижения.
    /// Возвращает только те, что открылись именно сейчас.
    /// </summary>
    /// <param name="notify">
    /// false — не показывать тост. Нужно окну достижений: оно лишь догоняет
    /// пропущенное и не должно сыпать уведомлениями при каждом открытии.
    /// </param>
    public static List<Achievement> Evaluate(bool notify = true)
    {
        var snapshot = BuildSnapshot();
        var fresh = new List<Achievement>();

        lock (Gate)
        {
            EnsureLoaded();

            var known = new HashSet<string>(_state!.Unlocked.Select(u => u.Id), StringComparer.Ordinal);

            foreach (var achievement in Catalog)
            {
                if (known.Contains(achievement.Id)) continue;

                int value;
                try { value = achievement.Measure(snapshot); } catch { continue; }
                if (value < achievement.Target) continue;

                _state.Unlocked.Add(new UnlockedAchievement { Id = achievement.Id, AtUtc = DateTime.UtcNow });
                known.Add(achievement.Id);
                fresh.Add(achievement);
            }

            if (fresh.Count > 0) Save();
        }

        if (notify && fresh.Count > 0) RaiseUnlocked(fresh);
        return fresh;
    }

    /// <summary>Сообщает об открытии в потоке UI: тост нельзя показывать из фонового потока.</summary>
    private static void RaiseUnlocked(List<Achievement> fresh)
    {
        var handler = Unlocked;
        if (handler == null) return;

        try
        {
            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
                app.Dispatcher.BeginInvoke(new Action(() => handler(fresh)));
            else
                handler(fresh);
        }
        catch { /* приложение уже закрывается */ }
    }

    /// <summary>Собирает статистику для проверки достижений.</summary>
    public static AchievementSnapshot BuildSnapshot()
    {
        var sessions = PlayStats.LoadAll();

        int totalSeconds = 0;
        int longest = 0;
        int launches = 0;
        bool early = false;

        var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var days = new HashSet<DateTime>();
        var perDay = new Dictionary<DateTime, HashSet<string>>();

        foreach (var session in sessions)
        {
            if (session.Seconds <= 0) continue;

            launches++;
            totalSeconds += session.Seconds;
            if (session.Seconds > longest) longest = session.Seconds;

            var title = session.Title ?? "";
            if (!string.IsNullOrWhiteSpace(title)) titles.Add(title);

            var local = session.StartedUtc.ToLocalTime();
            if (local.Hour < 8) early = true;

            var day = local.Date;
            days.Add(day);

            if (!perDay.TryGetValue(day, out var set))
                perDay[day] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(title)) set.Add(title);
        }

        var history = HistoryLog.ReadAll();
        var failDays = new HashSet<DateTime>();
        DateTime oldest = default;

        foreach (var entry in history)
        {
            var day = entry.Ts.ToLocalTime().Date;
            if (oldest == default || day < oldest) oldest = day;
            if (entry.Type is "unlock_fail" or "admin_fail") failDays.Add(day);
        }

        return new AchievementSnapshot
        {
            TotalSeconds = totalSeconds,
            TotalLaunches = launches,
            DistinctGames = titles.Count,
            LibraryCount = App.Settings.Games.Count,
            LongestSessionSeconds = longest,
            MaxGamesInDay = perDay.Count == 0 ? 0 : perDay.Values.Max(v => v.Count),
            MaxStreakDays = LongestStreak(days),
            EarlyBird = early,
            CleanDays = CleanDays(failDays, oldest, history.Count > 0),
        };
    }

    /// <summary>Самая длинная серия дней подряд, в которые была игра.</summary>
    private static int LongestStreak(HashSet<DateTime> days)
    {
        if (days.Count == 0) return 0;

        int best = 0;
        foreach (var day in days)
        {
            // Считаем только начало серии, чтобы не проходить её дважды.
            if (days.Contains(day.AddDays(-1))) continue;

            int length = 0;
            var cursor = day;
            while (days.Contains(cursor))
            {
                length++;
                cursor = cursor.AddDays(1);
            }

            if (length > best) best = length;
        }
        return best;
    }

    /// <summary>Сколько дней подряд (по сегодня) не было неудачных попыток ввода пароля.</summary>
    private static int CleanDays(HashSet<DateTime> failDays, DateTime oldest, bool hasHistory)
    {
        if (!hasHistory || oldest == default) return 0;

        int clean = 0;
        var cursor = DateTime.Today;

        while (clean < 60)
        {
            if (failDays.Contains(cursor)) break;
            clean++;
            cursor = cursor.AddDays(-1);

            // Дальше журнал не помнит — считать нечего.
            if (cursor < oldest) break;
        }

        return clean;
    }
}
