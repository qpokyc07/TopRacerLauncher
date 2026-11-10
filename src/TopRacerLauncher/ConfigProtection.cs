using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TopRacerLauncher;

/// <summary>
/// Защита настроек от ручного редактирования: критичные поля (пароль администратора,
/// блокировка, таймер) подписываются ключом установки. Если config.json поправили
/// в блокноте — подпись не сойдётся, и пароль администратора будет сброшен.
/// </summary>
public static class ConfigProtection
{
    private const string KeyFileName = "security.key";

    private static readonly string[] SignedFields =
    {
        "password", "salt", "hashVersion", "hashIterations", "locked", "timer", "sessionMinutes",
    };

    /// <summary>Подписывает настройки перед сохранением.</summary>
    public static void Apply(Settings settings)
    {
        try { settings.ProtectionStamp = Compute(settings); }
        catch { settings.ProtectionStamp = ""; }
    }

    /// <summary>
    /// Проверяет подпись. false — настройки правили вручную (или подписи ещё нет).
    /// </summary>
    public static bool Verify(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProtectionStamp)) return true;   // старый конфиг

        try
        {
            var expected = Compute(settings);
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(settings.ProtectionStamp));
        }
        catch { return true; }
    }

    /// <summary>Ключ установки: один раз создаётся в папке настроек.</summary>
    private static byte[] GetKey()
    {
        var path = Path.Combine(ConfigStore.ConfigDirectory, KeyFileName);

        try
        {
            Directory.CreateDirectory(ConfigStore.ConfigDirectory);

            if (!File.Exists(path))
            {
                var fresh = RandomNumberGenerator.GetBytes(32);
                File.WriteAllBytes(path, fresh);

                try { File.SetAttributes(path, FileAttributes.Hidden); } catch { }

                return fresh;
            }

            var bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 16) return bytes;
        }
        catch { /* нет доступа — используем запасной ключ */ }

        // Запасной вариант: ключ из имени компьютера и папки установки.
        return SHA256.HashData(Encoding.UTF8.GetBytes(
            Environment.MachineName + "|" + Environment.UserName + "|TopRacerLauncher"));
    }

    private static string Compute(Settings s)
    {
        var payload = new StringBuilder();

        payload.Append(s.PasswordHash).Append('|')
               .Append(s.PasswordSalt).Append('|')
               .Append(s.HashVersion).Append('|')
               .Append(s.HashIterations).Append('|')
               .Append(s.Locked).Append('|')
               .Append(s.TimerEnabled).Append('|')
               .Append(s.SessionMinutes);

        using var hmac = new HMACSHA256(GetKey());
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload.ToString())));
    }

    /// <summary>Что именно подписывается — для журнала и подсказок.</summary>
    public static string SignedFieldsText => string.Join(", ", SignedFields);
}
