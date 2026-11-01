using System.IO;
using System.Text;
using System.Text.Json;

namespace TopRacerLauncher;

/// <summary>Одна сыгранная сессия.</summary>
public sealed class PlaySession
{
    public DateTime StartedUtc { get; set; }
    public string Title { get; set; } = "";
    public int Seconds { get; set; }
}

/// <summary>Сводка по игре для таблицы статистики.</summary>
public sealed class GameStatRow
{
    public string Title { get; init; } = "";
    public int Seconds { get; init; }
    public int Launches { get; init; }
    public DateTime LastStartedUtc { get; init; }

    public string TimeText
    {
        get
        {
            var t = TimeSpan.FromSeconds(Seconds);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours} ч {t.Minutes:D2} мин"
                : $"{t.Minutes} мин";
        }
    }

    public string LaunchesText => Launches + " " + Plural(Launches, "запуск", "запуска", "запусков");

    public string LastText => LastStartedUtc == default
        ? ""
        : LastStartedUtc.ToLocalTime().ToString("dd.MM HH:mm");

    private static string Plural(int n, string one, string few, string many)
    {
        int mod100 = n % 100;
        int mod10 = n % 10;

        if (mod100 is >= 11 and <= 14) return many;
        return mod10 switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }
}

/// <summary>Учёт времени по играм (журнал сессий в %APPDATA%\TopRacerLauncher\playtime.jsonl).</summary>
public static class PlayStats
{
    private static readonly object Gate = new();
    private const int MaxLines = 5000;

    public static string FilePath => Path.Combine(ConfigStore.ConfigDirectory, "playtime.jsonl");

    public static void Add(string title, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        if (duration < TimeSpan.FromSeconds(5)) return;

        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(ConfigStore.ConfigDirectory);

                var session = new PlaySession
                {
                    StartedUtc = DateTime.UtcNow - duration,
                    Title = title,
                    Seconds = (int)duration.TotalSeconds,
                };

                File.AppendAllText(FilePath, JsonSerializer.Serialize(session) + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* статистика не критична для работы */ }
    }

    /// <summary>Все записанные сессии в порядке добавления (для достижений).</summary>
    public static List<PlaySession> LoadAll(int max = 20000)
    {
        var result = new List<PlaySession>();

        try
        {
            if (!File.Exists(FilePath)) return result;

            foreach (var line in File.ReadLines(FilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var session = JsonSerializer.Deserialize<PlaySession>(line);
                    if (session != null && !string.IsNullOrWhiteSpace(session.Title)) result.Add(session);
                }
                catch { /* битая строка — пропускаем */ }
            }
        }
        catch { /* нет доступа — пустая статистика */ }

        if (result.Count > max)
            result = result.Skip(result.Count - max).ToList();

        return result;
    }

    /// <summary>Сводка по играм за период (since == null — за всё время).</summary>
    public static List<GameStatRow> Aggregate(DateTime? since)
    {
        var byTitle = new Dictionary<string, (int Seconds, int Launches, DateTime Last)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!File.Exists(FilePath)) return new List<GameStatRow>();

            foreach (var line in File.ReadLines(FilePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                PlaySession? session;
                try { session = JsonSerializer.Deserialize<PlaySession>(line); }
                catch { continue; }

                if (session == null || string.IsNullOrWhiteSpace(session.Title)) continue;
                if (since.HasValue && session.StartedUtc.ToLocalTime() < since.Value) continue;

                if (byTitle.TryGetValue(session.Title, out var current))
                {
                    byTitle[session.Title] = (
                        current.Seconds + session.Seconds,
                        current.Launches + 1,
                        session.StartedUtc > current.Last ? session.StartedUtc : current.Last);
                }
                else
                {
                    byTitle[session.Title] = (session.Seconds, 1, session.StartedUtc);
                }
            }
        }
        catch { /* нет доступа — пустая статистика */ }

        return byTitle
            .Select(kv => new GameStatRow
            {
                Title = kv.Key,
                Seconds = kv.Value.Seconds,
                Launches = kv.Value.Launches,
                LastStartedUtc = kv.Value.Last,
            })
            .OrderByDescending(r => r.Seconds)
            .ToList();
    }

    public static TimeSpan TotalTime(IEnumerable<GameStatRow> rows)
        => TimeSpan.FromSeconds(rows.Sum(r => r.Seconds));

    /// <summary>Обрезает журнал, чтобы файл не рос бесконечно.</summary>
    public static void Trim()
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath)) return;

                var lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                if (lines.Length <= MaxLines) return;

                File.WriteAllLines(FilePath, lines.Skip(lines.Length - MaxLines), Encoding.UTF8);
            }
        }
        catch { /* не критично */ }
    }
}
