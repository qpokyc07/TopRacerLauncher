using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TopRacerLauncher;

/// <summary>Одна учётная запись Windows, с которой запускали лаунчер.</summary>
public sealed class UsageRecord
{
    public string User { get; set; } = "";
    public string Machine { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public int Launches { get; set; }
}

public sealed class UsageDb
{
    public List<UsageRecord> Records { get; set; } = new();

    /// <summary>Метки запусков (последние 500) — для «как часто».</summary>
    public List<DateTime> Launches { get; set; } = new();
}

/// <summary>Сводка использования для админ-меню.</summary>
public sealed record UsageSummary(
    int TotalLaunches,
    int Installs,
    int LaunchesLast7Days,
    DateTime? FirstSeenUtc,
    DateTime? LastSeenUtc,
    IReadOnlyList<UsageRecord> Records);

/// <summary>
/// Локальная статистика: сколько раз запускали, с каких учётных записей и машин.
/// Файл общий для всех пользователей Windows на этом ПК (%PROGRAMDATA%),
/// поэтому видно, кто ещё пользуется лаунчером.
/// </summary>
public static class UsageStats
{
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DataDirectory { get; } = ResolveDirectory();

    public static string FilePath => Path.Combine(DataDirectory, "usage.json");

    private static string ResolveDirectory()
    {
        try
        {
            var common = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "TopRacerLauncher");
            Directory.CreateDirectory(common);

            // Проверяем, что писать действительно можно (иначе — в профиль).
            var probe = Path.Combine(common, ".write-test");
            File.WriteAllText(probe, "1");
            File.Delete(probe);

            return common;
        }
        catch
        {
            return ConfigStore.ConfigDirectory;
        }
    }

    private static UsageDb Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new UsageDb();
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<UsageDb>(json, JsonOptions) ?? new UsageDb();
        }
        catch
        {
            return new UsageDb();
        }
    }

    private static void Save(UsageDb db)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(db, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* статистика не критична */ }
    }

    /// <summary>Отмечает запуск приложения и возвращает сводку.</summary>
    public static UsageSummary RecordLaunch()
    {
        lock (Gate)
        {
            var db = Load();
            var user = Environment.UserName;
            var machine = Environment.MachineName;
            var version = typeof(UsageStats).Assembly.GetName().Version?.ToString(3) ?? "";

            var record = db.Records.FirstOrDefault(r =>
                string.Equals(r.User, user, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Machine, machine, StringComparison.OrdinalIgnoreCase));

            if (record == null)
            {
                record = new UsageRecord { User = user, Machine = machine, FirstSeenUtc = DateTime.UtcNow };
                db.Records.Add(record);
            }

            record.Version = version;
            record.LastSeenUtc = DateTime.UtcNow;
            record.Launches++;

            db.Launches.Add(DateTime.UtcNow);
            if (db.Launches.Count > 500)
                db.Launches.RemoveRange(0, db.Launches.Count - 500);

            Save(db);
            return Summarize(db);
        }
    }

    /// <summary>Сводка без отметки запуска.</summary>
    public static UsageSummary Read()
    {
        lock (Gate)
        {
            return Summarize(Load());
        }
    }

    private static UsageSummary Summarize(UsageDb db)
    {
        var since = DateTime.UtcNow.AddDays(-7);

        return new UsageSummary(
            TotalLaunches: db.Records.Sum(r => r.Launches),
            Installs: db.Records.Count,
            LaunchesLast7Days: db.Launches.Count(t => t >= since),
            FirstSeenUtc: db.Records.Count == 0 ? null : db.Records.Min(r => r.FirstSeenUtc),
            LastSeenUtc: db.Records.Count == 0 ? null : db.Records.Max(r => r.LastSeenUtc),
            Records: db.Records.OrderByDescending(r => r.LastSeenUtc).ToList());
    }
}
