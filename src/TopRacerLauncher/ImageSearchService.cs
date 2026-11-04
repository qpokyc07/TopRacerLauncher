using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace TopRacerLauncher;

/// <summary>Результат поиска картинки в интернете.</summary>
public sealed record ImageSearchItem(string AppId, string Name, string ThumbnailUrl);

/// <summary>Какая картинка ищется.</summary>
public enum ImageKind
{
    /// <summary>Обложка игры (портретная).</summary>
    Cover,

    /// <summary>Задний фон (широкий).</summary>
    Background,
}

/// <summary>Скачанные картинки для игры.</summary>
public sealed record GameArt(string? CoverPath, string? BackgroundPath, string AppId);

/// <summary>
/// Поиск обложек и фонов игр через открытый магазин Steam (без ключей и регистрации).
/// </summary>
public static class ImageSearchService
{
    private static readonly HttpClient Http = CreateClient();

    /// <summary>Слова, по которым видно, что это не сама игра, а дополнение/набор/саундтрек.</summary>
    private static readonly string[] NonGameWords =
    {
        "soundtrack", " official ost", " dlc", "season pass", "bundle", "expansion",
        "cosmetic", "artbook", "art book", "upgrade", "starter pack", "points pack",
        "currency", "demo", "playtest", "dedicated server", " sdk", "editor",
    };

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("TopRacerLauncher/1.1");
        return c;
    }

    /// <summary>
    /// Ищет игры по названию. Возвращает только игры (без DLC/саундтреков/наборов),
    /// точное совпадение названия — первым.
    /// </summary>
    public static async Task<List<ImageSearchItem>> SearchAsync(string query, CancellationToken ct)
    {
        var url = "https://store.steampowered.com/api/storesearch/?term=" +
                  Uri.EscapeDataString(query) + "&l=english&cc=us";

        var json = await Http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);

        var items = new List<ImageSearchItem>();
        if (!doc.RootElement.TryGetProperty("items", out var arr))
            return items;

        foreach (var it in arr.EnumerateArray())
        {
            if (!it.TryGetProperty("id", out var idEl)) continue;
            var id = idEl.ToString();
            var name = it.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var thumb = it.TryGetProperty("tiny_image", out var t) ? t.GetString() ?? "" : "";
            if (thumb.Length == 0 || name.Length == 0) continue;

            items.Add(new ImageSearchItem(id, name, thumb));
            if (items.Count >= 12) break;
        }

        if (items.Count == 0) return items;

        // Отсеиваем DLC/наборы/саундтреки по названию и ставим точное совпадение первым.
        // Не ходим в appdetails за каждым результатом: это ест лимит запросов Steam
        // и из-за этого падают и метаданные, и обложки.
        var clean = items.Where(i => !LooksLikeExtra(i.Name)).ToList();
        var pool = clean.Count > 0 ? clean : items;

        var q = query.Trim();
        return pool
            .OrderByDescending(i => string.Equals(i.Name.Trim(), q, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(i => i.Name.Trim().StartsWith(q, StringComparison.OrdinalIgnoreCase))
            .ThenBy(i => i.Name.Length)
            .ToList();
    }

    private static bool LooksLikeExtra(string name)
    {
        var lower = " " + name.ToLowerInvariant() + " ";
        return NonGameWords.Any(w => lower.Contains(w, StringComparison.Ordinal));
    }

    /// <summary>Для самопроверки: похоже ли название на DLC/саундтрек, а не на игру.</summary>
    internal static bool IsLikelyExtraName(string name) => LooksLikeExtra(name);

    /// <summary>
    /// Подбирает и скачивает обложку и фон для игры: по appid напрямую,
    /// иначе — через поиск по названию. null, если ничего не нашлось.
    /// </summary>
    public static async Task<GameArt?> FetchArtAsync(string title, string? knownAppId, CancellationToken ct)
    {
        string appId;

        if (!string.IsNullOrWhiteSpace(knownAppId) && knownAppId.All(char.IsDigit))
        {
            appId = knownAppId;
        }
        else
        {
            var results = await SearchAsync(title, ct);
            var best = results.FirstOrDefault();
            if (best == null) return null;
            appId = best.AppId;
        }

        var cover = await TryDownloadArtAsync(appId, ImageKind.Cover, ct);
        var background = await TryDownloadArtAsync(appId, ImageKind.Background, ct);

        // По appid ничего нет — ищем игру по названию и пробуем лучшие совпадения.
        // Так обложка подтягивается автоматически, даже если appid устарел или ошибочный.
        if (cover == null && background == null && !string.IsNullOrWhiteSpace(title))
        {
            var found = await SearchAsync(title, ct);

            foreach (var candidate in found.Take(3))
            {
                if (string.Equals(candidate.AppId, appId, StringComparison.Ordinal)) continue;

                cover = await TryDownloadArtAsync(candidate.AppId, ImageKind.Cover, ct);
                background ??= await TryDownloadArtAsync(candidate.AppId, ImageKind.Background, ct);

                if (cover != null || background != null)
                {
                    appId = candidate.AppId;
                    break;
                }
            }
        }

        if (cover == null && background == null) return null;
        return new GameArt(cover, background, appId);
    }

    /// <summary>
    /// Загружает превью асинхронно (не на UI-потоке) и декодирует с уменьшением.
    /// Возвращает null, если картинку не удалось прочитать.
    /// </summary>
    public static async Task<BitmapImage?> LoadThumbnailAsync(string url, int decodeWidth, CancellationToken ct)
    {
        try
        {
            var bytes = await Http.GetByteArrayAsync(url, ct);
            if (bytes.Length < 256) return null;

            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = Math.Max(1, decodeWidth);
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Скачивает лучшую доступную картинку и сохраняет в локальную папку.</summary>
    public static async Task<string> DownloadAsync(ImageSearchItem item, ImageKind kind, CancellationToken ct)
    {
        var path = await TryDownloadArtAsync(item.AppId, kind, ct);
        return path ?? throw new InvalidOperationException("Не удалось скачать картинку из интернета.");
    }

    /// <summary>Пытается скачать обложку/фон по appid. null — ничего не нашлось.</summary>
    public static async Task<string?> TryDownloadArtAsync(string appId, ImageKind kind, CancellationToken ct)
    {
        var cdn = $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/";
        string[] urls = kind == ImageKind.Cover
            ? new[] { cdn + "library_600x900.jpg", cdn + "header.jpg", cdn + "capsule_616x353.jpg" }
            : new[] { cdn + "library_hero.jpg", cdn + "page_bg_generated_v6b.jpg", cdn + "header.jpg" };

        foreach (var url in urls)
        {
            try
            {
                var bytes = await Http.GetByteArrayAsync(url, ct);
                if (bytes.Length <= 1000) continue;

                var dir = MediaDirectory;
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, Guid.NewGuid().ToString("N") + ExtensionFor(bytes));
                await File.WriteAllBytesAsync(path, bytes, ct);
                return path;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // пробуем следующий размер
            }
        }

        return null;
    }

    /// <summary>Расширение по сигнатуре файла — чтобы .jpg не оказывался PNG.</summary>
    private static string ExtensionFor(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ".png";
        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return ".gif";
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
            return ".bmp";
        return ".jpg";
    }

    public static string MediaDirectory =>
        Path.Combine(ConfigStore.ConfigDirectory, "media");

    /// <summary>Удаляет локальный файл картинки, если он лежит в папке медиа (не трогаем свои).</summary>
    public static void DeleteIfMedia(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || path.Contains("://", StringComparison.Ordinal)) return;

            var full = Path.GetFullPath(path);
            var media = Path.GetFullPath(MediaDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            // Сравнение с разделителем: ...\media2\x.jpg не должен считаться «внутри media».
            if (full.StartsWith(media, StringComparison.OrdinalIgnoreCase) && File.Exists(full))
                File.Delete(full);
        }
        catch { /* не критично */ }
    }

    /// <summary>Убирает из папки медиа файлы, на которые не ссылается ни одна игра.</summary>
    public static void CleanupOrphans(IEnumerable<string> keepPaths)
    {
        try
        {
            var dir = MediaDirectory;
            if (!Directory.Exists(dir)) return;

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in keepPaths)
            {
                if (string.IsNullOrWhiteSpace(p) || p.Contains("://", StringComparison.Ordinal)) continue;
                try { keep.Add(Path.GetFullPath(p)); } catch { /* некорректный путь — пропускаем */ }
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                string full;
                try { full = Path.GetFullPath(file); } catch { continue; }
                if (keep.Contains(full)) continue;

                // Свежие файлы не трогаем: возможно, идёт скачивание прямо сейчас.
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(full) < TimeSpan.FromMinutes(5)) continue;
                    File.Delete(full);
                }
                catch { /* файл занят — пропускаем */ }
            }
        }
        catch { /* уборка не критична */ }
    }
}
