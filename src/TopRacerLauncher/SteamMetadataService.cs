using System.Net.Http;
using System.Text.Json;

namespace TopRacerLauncher;

/// <summary>Метаданные игры из магазина Steam.</summary>
public sealed record SteamMeta(
    string AppId,
    string Name,
    string Description,
    string Developers,
    string Publishers,
    string Genres,
    string ReleaseDate,
    string Year,
    int? Metacritic);

/// <summary>Загрузка описания, разработчика, жанров и даты выхода из Steam.</summary>
public static class SteamMetadataService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("TopRacerLauncher/1.2");
        return c;
    }

    /// <summary>
    /// Загружает метаданные по appid (или находит игру по названию).
    /// Возвращает null, если ничего не нашлось.
    /// </summary>
    public static async Task<SteamMeta?> FetchAsync(string? appId, string title, CancellationToken ct)
    {
        var id = appId?.Trim() ?? "";

        if (id.Length == 0 || !id.All(char.IsDigit))
        {
            var found = await ImageSearchService.SearchAsync(title, ct);
            id = found.FirstOrDefault()?.AppId ?? "";
        }

        if (id.Length == 0) return null;

        // Одна попытка с коротким повтором: Steam иногда отвечает 429/пустотой.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var url = $"https://store.steampowered.com/api/appdetails?appids={id}&cc=ru&l=russian";
                var json = await Http.GetStringAsync(url, ct);

                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(id, out var entry)) return null;
                if (!entry.TryGetProperty("success", out var success) || !success.GetBoolean()) return null;
                if (!entry.TryGetProperty("data", out var data)) return null;

                string Text(string prop) =>
                    data.TryGetProperty(prop, out var el) && el.ValueKind == JsonValueKind.String
                        ? el.GetString() ?? ""
                        : "";

                // developers/publishers — массивы строк.
                string JoinStrings(string prop)
                {
                    if (!data.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
                        return "";
                    var names = arr.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString())
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    return string.Join(", ", names!);
                }

                // genres — массив объектов {id, description}.
                string JoinGenres()
                {
                    if (!data.TryGetProperty("genres", out var arr) || arr.ValueKind != JsonValueKind.Array)
                        return "";
                    var names = arr.EnumerateArray()
                        .Select(x => x.TryGetProperty("description", out var d) ? d.GetString() : null)
                        .Where(s => !string.IsNullOrWhiteSpace(s));
                    return string.Join(", ", names!);
                }

                var description = Text("short_description");
                if (description.Length == 0)
                    description = StripHtml(Text("detailed_description"));

                var release = "";
                var year = "";
                if (data.TryGetProperty("release_date", out var rd) && rd.ValueKind == JsonValueKind.Object)
                {
                    release = rd.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                    var m = System.Text.RegularExpressions.Regex.Match(release, @"\b(19|20)\d{2}\b");
                    if (m.Success) year = m.Value;
                }

                int? score = null;
                if (data.TryGetProperty("metacritic", out var mc) && mc.ValueKind == JsonValueKind.Object &&
                    mc.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number)
                {
                    score = sc.GetInt32();
                }

                return new SteamMeta(
                    AppId: id,
                    Name: Text("name"),
                    Description: Trim(description, 900),
                    Developers: JoinStrings("developers"),
                    Publishers: JoinStrings("publishers"),
                    Genres: JoinGenres(),
                    ReleaseDate: release,
                    Year: year,
                    Metacritic: score);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (attempt == 1) return null;
                await Task.Delay(800, ct);
            }
        }

        return null;
    }

    /// <summary>Записывает метаданные в игру (не затирая уже заполненное пустым).</summary>
    public static void Apply(GameItem game, SteamMeta meta)
    {
        if (!string.IsNullOrWhiteSpace(meta.Name) && string.IsNullOrWhiteSpace(game.Title))
            game.Title = meta.Name;

        if (!string.IsNullOrWhiteSpace(meta.Description)) game.Description = meta.Description;
        if (!string.IsNullOrWhiteSpace(meta.Developers)) game.Developer = meta.Developers;
        if (!string.IsNullOrWhiteSpace(meta.Publishers)) game.Publisher = meta.Publishers;
        if (!string.IsNullOrWhiteSpace(meta.Genres)) game.Genres = meta.Genres;
        if (!string.IsNullOrWhiteSpace(meta.Year)) game.ReleaseYear = meta.Year;
        if (meta.Metacritic.HasValue) game.Metacritic = meta.Metacritic;
        if (!string.IsNullOrWhiteSpace(meta.AppId)) game.AppId = meta.AppId;
    }

    private static string Trim(string text, int max)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";

        html = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        html = System.Net.WebUtility.HtmlDecode(html);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\s+", " ");
        return html.Trim();
    }
}
