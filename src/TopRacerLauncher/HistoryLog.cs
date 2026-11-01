using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TopRacerLauncher;

/// <summary>Одна запись журнала событий.</summary>
public sealed class HistoryEntry
{
    public DateTime Ts { get; set; }
    public string Type { get; set; } = "";
    public string? Title { get; set; }
    public int Seconds { get; set; }
    public string? Detail { get; set; }
}

/// <summary>
/// Журнал событий (запуски, истечения сеанса, неудачные попытки входа).
/// Формат — JSON Lines: одна строка на событие, дописываем в конец, ничего не перечитывая.
/// </summary>
public static class HistoryLog
{
    private const long MaxFileBytes = 1_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string FilePath => Path.Combine(ConfigStore.ConfigDirectory, "history.jsonl");

    private static string OldFilePath => FilePath + ".old";

    /// <summary>Добавляет событие в журнал. Ошибки записи не влияют на работу лаунчера.</summary>
    public static void Add(string type, string? title = null, int seconds = 0, string? detail = null)
    {
        try
        {
            var entry = new HistoryEntry
            {
                Ts = DateTime.UtcNow,
                Type = type,
                Title = string.IsNullOrWhiteSpace(title) ? null : title,
                Seconds = seconds,
                Detail = string.IsNullOrWhiteSpace(detail) ? null : detail,
            };

            var line = JsonSerializer.Serialize(entry, JsonOptions);

            Directory.CreateDirectory(ConfigStore.ConfigDirectory);

            // Ротация: журнал не должен расти бесконечно.
            try
            {
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxFileBytes)
                    File.Move(FilePath, OldFilePath, overwrite: true);
            }
            catch { /* ротация не критична */ }

            File.AppendAllText(FilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* журнал не должен ломать лаунчер */ }
    }

    /// <summary>Читает последние записи (новые — первыми).</summary>
    public static List<HistoryEntry> ReadLast(int max = 200)
    {
        var result = new List<HistoryEntry>();
        try
        {
            if (!File.Exists(FilePath)) return result;

            var lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            for (int i = lines.Length - 1; i >= 0 && result.Count < max; i--)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<HistoryEntry>(line, JsonOptions);
                    if (entry != null) result.Add(entry);
                }
                catch { /* битая строка — пропускаем */ }
            }
        }
        catch { /* нет доступа — покажем пустой список */ }
        return result;
    }

    /// <summary>
    /// Все записи журнала в хронологическом порядке, включая файл после ротации.
    /// Нужно достижениям: им важен весь период, а не последние 200 событий.
    /// </summary>
    public static List<HistoryEntry> ReadAll(int max = 20000)
    {
        var result = new List<HistoryEntry>();

        foreach (var path in new[] { OldFilePath, FilePath })
        {
            try
            {
                if (!File.Exists(path)) continue;

                foreach (var line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<HistoryEntry>(line, JsonOptions);
                        if (entry != null) result.Add(entry);
                    }
                    catch { /* битая строка — пропускаем */ }
                }
            }
            catch { /* нет доступа — пропускаем файл */ }
        }

        if (result.Count > max)
            result = result.Skip(result.Count - max).ToList();

        return result;
    }

    /// <summary>Человекочитаемое описание события для списка в админ-меню.</summary>
    public static string Describe(HistoryEntry e)
    {
        var time = e.Ts.ToLocalTime().ToString("dd.MM HH:mm");
        var title = string.IsNullOrEmpty(e.Title) ? "" : " «" + e.Title + "»";

        return e.Type switch
        {
            "session_start" => $"{time}  •  выдан сеанс {FormatMinutes(e.Seconds)}",
            "session_extend" => $"{time}  •  сеанс продлён{title}" + (e.Detail is null ? "" : " (" + e.Detail + ")"),
            "expire" => $"{time}  •  время истекло, доступ закрыт",
            "game_start" => $"{time}  •  запуск{title}",
            "game_kill" => $"{time}  •  игра закрыта{title}",
            "unlock_ok" => $"{time}  •  вход по паролю",
            "unlock_fail" => $"{time}  •  неверный пароль" + (e.Detail is null ? "" : " (" + e.Detail + ")"),
            "admin_fail" => $"{time}  •  неверный пароль в админ-меню" + (e.Detail is null ? "" : " (" + e.Detail + ")"),
            "admin_ok" => $"{time}  •  открыто админ-меню",
            "time_set" => $"{time}  •  выдано {FormatMinutes(e.Seconds)}",
            "time_reset" => $"{time}  •  время сброшено вручную",
            "games_added" => $"{time}  •  добавлено игр: {e.Detail}",
            "covers" => $"{time}  •  подтянуты обложки: {e.Detail}",
            "config_recovered" => $"{time}  •  конфиг восстановлен из резервной копии",
            "password_changed" => $"{time}  •  пароль изменён",
            _ => $"{time}  •  {e.Type}" + (e.Detail is null ? "" : " " + e.Detail),
        };
    }

    private static string FormatMinutes(int seconds)
    {
        if (seconds <= 0) return "—";
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours} ч {t.Minutes} мин"
            : $"{t.Minutes} мин";
    }
}
