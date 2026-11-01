using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace TopRacerLauncher;

/// <summary>Сведения об обновлении из update.json.</summary>
public sealed class UpdateInfo
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";
}

/// <summary>
/// Проверка и установка обновлений. Источник — URL на update.json или папка
/// (в том числе сетевая), где лежат update.json и новый exe.
/// </summary>
public static class UpdateService
{
    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Скачанный файл обновления (null — ничего не скачано).</summary>
    public static string? PendingFile { get; private set; }

    public static bool IsNewer(string remote, string current)
    {
        if (!Version.TryParse(remote, out var r)) return false;
        if (!Version.TryParse(current, out var c)) return true;
        return r > c;
    }

    public static async Task<UpdateInfo?> CheckAsync(string source, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        try
        {
            string json;

            if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                json = await http.GetStringAsync(source, ct);
            }
            else
            {
                var path = File.Exists(source) ? source : Path.Combine(source, "update.json");
                if (!File.Exists(path)) return null;
                json = await File.ReadAllTextAsync(path, ct);
            }

            var info = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            return info != null && IsNewer(info.Version, CurrentVersion) ? info : null;
        }
        catch
        {
            return null;   // нет сети / битый манифест — молча живём дальше
        }
    }

    /// <summary>Скачивает новый exe рядом с манифестом и готовит установку.</summary>
    public static async Task<bool> DownloadAsync(UpdateInfo info, string source, IProgress<int>? progress, CancellationToken ct)
    {
        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "TopRacerLauncher-update");
            Directory.CreateDirectory(folder);

            var target = Path.Combine(folder, "TopRacerLauncher.new.exe");
            if (File.Exists(target)) File.Delete(target);

            if (info.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                using var response = await http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var total = response.Content.Headers.ContentLength ?? -1;
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(target);

                var buffer = new byte[81920];
                long done = 0;
                int read;

                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;

                    if (total > 0) progress?.Report((int)(done * 100 / total));
                }
            }
            else
            {
                // Локальная или сетевая папка: манифест рядом с новым exe.
                var local = Path.IsPathRooted(info.Url) ? info.Url : Path.Combine(source, info.Url);
                File.Copy(local, target, overwrite: true);
                progress?.Report(100);
            }

            PendingFile = target;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Запускает скрипт замены exe и закрывает приложение.</summary>
    public static bool ApplyAndRestart()
    {
        if (PendingFile == null || !File.Exists(PendingFile)) return false;

        try
        {
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExe)) return false;

            var script = Path.Combine(Path.GetDirectoryName(PendingFile)!, "apply-update.cmd");

            var lines = new[]
            {
                "@echo off",
                "timeout /t 2 /nobreak >nul",
                ":wait",
                "tasklist /fi \"imagename eq TopRacerLauncher.exe\" | find /i \"TopRacerLauncher.exe\" >nul",
                "if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)",
                "copy /y \"" + PendingFile + "\" \"" + currentExe + "\" >nul",
                "start \"\" \"" + currentExe + "\"",
                "del \"%~f0\"",
            };

            File.WriteAllLines(script, lines);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });

            return true;
        }
        catch
        {
            return false;
        }
    }
}
