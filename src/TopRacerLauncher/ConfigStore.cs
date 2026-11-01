using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TopRacerLauncher;

/// <summary>Загрузка и сохранение конфига лаунчера.</summary>
public static class ConfigStore
{
    /// <summary>Версия схемы хэширования пароля: 2 = PBKDF2-SHA256, 1 (или 0) = старый одиночный SHA-256.</summary>
    public const int CurrentHashVersion = 2;

    /// <summary>Итераций PBKDF2 по умолчанию (OWASP-уровень для SHA-256).</summary>
    public const int DefaultHashIterations = 210_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string ConfigDirectory { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TopRacerLauncher");

    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    /// <summary>Резервная копия предыдущего удачного конфига.</summary>
    public static string BackupPath => ConfigPath + ".bak";

    /// <summary>
    /// Неустранимая ошибка чтения конфига: файл был, но не читается и резервной копии нет.
    /// Пока это не null, сохранять нельзя — иначе затрём единственную копию данных.
    /// </summary>
    public static string? FatalError { get; private set; }

    /// <summary>Некритичное предупреждение при загрузке (например, восстановились из бэкапа).</summary>
    public static string? Warning { get; private set; }

    /// <summary>Для режима самопроверки: временный каталог, чтобы не трогать реальный конфиг.</summary>
    public static void UseDirectory(string directory) => ConfigDirectory = directory;

    /// <summary>Сбрасывает состояние ошибок (используется самопроверкой).</summary>
    internal static void ClearLoadState()
    {
        FatalError = null;
        Warning = null;
    }

    public static Settings Load()
    {
        FatalError = null;
        Warning = null;

        if (!File.Exists(ConfigPath))
        {
            // Конфига нет — первый запуск. Если осталась только резервная копия, поднимем её.
            if (TryLoadFile(BackupPath, out var backup, out _))
            {
                Warning = "Основной конфиг не найден, загружена резервная копия.";
                return backup!;
            }
            return new Settings();
        }

        if (TryLoadFile(ConfigPath, out var settings, out var error))
            return settings!;

        // Основной файл битый или недоступен. Пробуем .bak, а испорченный файл откладываем в сторону.
        if (TryLoadFile(BackupPath, out var fromBackup, out _))
        {
            Quarantine(ConfigPath);
            Warning = $"Основной конфиг повреждён ({error}). Загружена резервная копия.";
            return fromBackup!;
        }

        Quarantine(ConfigPath);
        FatalError = error;
        return new Settings();
    }

    private static bool TryLoadFile(string path, out Settings? settings, out string? error)
    {
        settings = null;
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                error = "файл не найден";
                return false;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "файл пустой";
                return false;
            }

            var loaded = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
            if (loaded == null)
            {
                error = "не удалось разобрать JSON";
                return false;
            }

            loaded.Games ??= new List<GameItem>();
            Normalize(loaded);
            settings = loaded;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Приводит настройки в допустимый вид и мигрирует старые форматы.</summary>
    private static void Normalize(Settings s)
    {
        s.SessionMinutes = Math.Clamp(s.SessionMinutes, 30, 240);
        s.CardWidth = Math.Clamp(s.CardWidth <= 0 ? 178 : s.CardWidth, 100, 380);
        if (string.IsNullOrWhiteSpace(s.AccentTheme)) s.AccentTheme = "blue";
        if (string.IsNullOrWhiteSpace(s.CardAspect)) s.CardAspect = "3:4";

        s.Games = s.Games
            .Where(g => g != null && !string.IsNullOrWhiteSpace(g.ExecutablePath))
            .ToList();

        // Миграция: раньше хранился только счётчик секунд без дедлайна.
        if (s.SessionEndUtc == default && s.RemainingSeconds > 0)
            s.SessionEndUtc = DateTime.UtcNow.AddSeconds(s.RemainingSeconds);

        if (s.TimerPaused && s.PausedRemainingSeconds <= 0)
            s.PausedRemainingSeconds = Math.Max(0, s.RemainingSeconds);
    }

    /// <summary>Откладывает испорченный файл, чтобы он не потерялся и не мешал.</summary>
    private static void Quarantine(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            var target = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Move(path, target, overwrite: true);
        }
        catch { /* не критично: главное — не потерять данные молча */ }
    }

    /// <summary>Сохраняет настройки. Возвращает false, если запись не удалась.</summary>
    public static bool Save(Settings settings)
    {
        // Не затираем конфиг, который не смогли прочитать: пусть пользователь решает.
        if (FatalError != null) return false;

        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            settings.SavedAtUtc = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, json, Encoding.UTF8);

            // Резервная копия предыдущей удачной версии.
            try
            {
                if (File.Exists(ConfigPath)) File.Copy(ConfigPath, BackupPath, overwrite: true);
            }
            catch { /* бэкап не критичен */ }

            // Атомарная замена на том же томе (в отличие от File.Copy).
            File.Move(tmp, ConfigPath, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // --- Пароли (закрытая система) ---

    public static string CreateSalt() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    /// <summary>PBKDF2-SHA256 — медленный хэш, в отличие от «сырого» SHA-256.</summary>
    public static string HashPassword(string password, string salt, int iterations = DefaultHashIterations)
    {
        var saltBytes = Convert.FromHexString(salt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), saltBytes, iterations, HashAlgorithmName.SHA256, 32);
        return Convert.ToHexString(hash);
    }

    /// <summary>Старый формат (одиночный SHA-256) — только для проверки и миграции.</summary>
    private static string LegacyHash(string password, string salt)
    {
        var bytes = Encoding.UTF8.GetBytes(salt + "||" + password);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    /// <summary>Проверяет пароль и прозрачно обновляет старый хэш до PBKDF2.</summary>
    public static bool VerifyPassword(Settings settings, string password)
    {
        try
        {
            if (!settings.PasswordSet) return false;

            var iterations = settings.HashIterations > 0 ? settings.HashIterations : DefaultHashIterations;
            var candidate = settings.HashVersion >= CurrentHashVersion
                ? HashPassword(password, settings.PasswordSalt, iterations)
                : LegacyHash(password, settings.PasswordSalt);

            if (!FixedTimeEquals(candidate, settings.PasswordHash)) return false;

            // Успешный вход со старым хэшем — сразу переводим на PBKDF2.
            if (settings.HashVersion < CurrentHashVersion)
            {
                SetPassword(settings, password);
                Save(settings);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SetPassword(Settings settings, string password)
    {
        settings.PasswordSalt = CreateSalt();
        settings.HashIterations = DefaultHashIterations;
        settings.PasswordHash = HashPassword(password, settings.PasswordSalt, settings.HashIterations);
        settings.HashVersion = CurrentHashVersion;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        byte[] left, right;
        try
        {
            left = Convert.FromHexString(a);
            right = Convert.FromHexString(b);
        }
        catch
        {
            return false;
        }
        return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
