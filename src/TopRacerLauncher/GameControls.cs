namespace TopRacerLauncher;

/// <summary>
/// Подсказка по управлению для игры: мини-клавиатура с нужными клавишами
/// и короткая строка-напоминание. Показывается на табличке запуска.
/// </summary>
public static class GameControls
{
    /// <summary>Вид раскладки: kind — жанр, note — подсказка, rows — ряды клавиш.</summary>
    public sealed record Hint(string Kind, string Note, string[][] Rows);

    private static readonly string[] Fps =
    {
        "counter-strike", "cs2", "cs:", "cs 1.6", "valorant", "doom", "quake", "apex", "overwatch",
        "battlefield", "call of duty", "warzone", "tarkov", "warface", "crossfire", "point blank",
        "war thunder", "warthunder", "world of tanks", "crossout", "enlisted",
    };

    private static readonly string[] Moba =
    {
        "dota", "league of legends", "smite", "heroes of", "париж",
    };

    private static readonly string[] Racing =
    {
        "need for speed", "forza", "assetto", "dirt", "rally", "trackmania", "f1 ", "car mechanic",
        "euro truck", "american truck", "beam", "gran turismo", "wreckfest", "snowrunner",
    };

    private static readonly string[] Survival =
    {
        "minecraft", "terraria", "rust", "forest", "valheim", "raft", "7 days", "dayz", "scum",
    };

    private static readonly string[] Party =
    {
        "among us", "goose goose", "fall guys", "pummel", "roblox", "human fall", "gang beasts",
        "project playtime", "poppy playtime", "ultimate custom night", "fnaf",
    };

    /// <summary>Подсказка для игры: свои клавиши (если заданы) важнее жанра.</summary>
    public static Hint For(string? title, string? custom = null)
    {
        var manual = ParseKeys(custom);
        if (manual.Count > 0)
            return new Hint("manual", "Клавиши заданы вручную в админ-меню.", Chunk(manual, 6));

        var text = (title ?? "").ToLowerInvariant();

        if (Contains(text, Fps))
        {
            return new Hint("fps",
                "Мышь — обзор и стрельба, колесо — оружие, Shift — бег",
                new[]
                {
                    new[] { "W" },
                    new[] { "A", "S", "D" },
                    new[] { "SHIFT", "SPACE", "CTRL" },
                    new[] { "E", "R", "Q", "G" },
                    new[] { "ЛКМ", "ПКМ", "КОЛЕСО" },
                });
        }

        if (Contains(text, Moba))
        {
            return new Hint("moba",
                "ПКМ — идти, ЛКМ — атака, Q W E R — способности",
                new[]
                {
                    new[] { "Q", "W", "E", "R" },
                    new[] { "A", "S", "D", "F" },
                    new[] { "B", "T", "C" },
                    new[] { "ЛКМ", "ПКМ" },
                });
        }

        if (Contains(text, Racing))
        {
            return new Hint("racing",
                "Газ — W или ↑, тормоз — S или ↓, руль — A/D, ручник — Space",
                new[]
                {
                    new[] { "↑" },
                    new[] { "←", "↓", "→" },
                    new[] { "W", "S" },
                    new[] { "SPACE", "SHIFT", "C" },
                });
        }

        if (Contains(text, Survival))
        {
            return new Hint("survival",
                "WASD — движение, мышь — обзор, E — взять/использовать, колесо — предметы",
                new[]
                {
                    new[] { "W" },
                    new[] { "A", "S", "D" },
                    new[] { "SHIFT", "SPACE", "E" },
                    new[] { "ЛКМ", "ПКМ" },
                });
        }

        if (Contains(text, Party))
        {
            return new Hint("party",
                "Стрелки или WASD — движение, Space — действие",
                new[]
                {
                    new[] { "W", "A", "S", "D" },
                    new[] { "←", "↑", "↓", "→" },
                    new[] { "SPACE", "E", "TAB" },
                });
        }

        return new Hint("common",
            "Стандартная раскладка: WASD или стрелки — движение, мышь — обзор, Esc — меню",
            new[]
            {
                new[] { "W" },
                new[] { "A", "S", "D" },
                new[] { "SPACE", "SHIFT", "ESC" },
                new[] { "ЛКМ", "ПКМ" },
            });
    }

    /// <summary>Клавиши для оверлея: свои (если заданы) или из пресета жанра.</summary>
    public static IReadOnlySet<string> UsedKeys(string? title, string? custom = null)
    {
        var hint = For(title, custom);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in hint.Rows)
            foreach (var key in row)
                set.Add(key);

        return set;
    }

    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wasd"] = new[] { "W", "A", "S", "D" },
        ["мышь"] = new[] { "ЛКМ", "ПКМ", "КОЛЕСО" },
        ["mouse"] = new[] { "ЛКМ", "ПКМ", "КОЛЕСО" },
        ["лкм"] = new[] { "ЛКМ" },
        ["пкм"] = new[] { "ПКМ" },
        ["колесо"] = new[] { "КОЛЕСО" },
        ["пробел"] = new[] { "SPACE" },
        ["space"] = new[] { "SPACE" },
        ["шифт"] = new[] { "SHIFT" },
        ["shift"] = new[] { "SHIFT" },
        ["контрл"] = new[] { "CTRL" },
        ["ctrl"] = new[] { "CTRL" },
        ["esc"] = new[] { "ESC" },
        ["tab"] = new[] { "TAB" },
        ["enter"] = new[] { "ENTER" },
        ["вверх"] = new[] { "↑" },
        ["вниз"] = new[] { "↓" },
        ["влево"] = new[] { "←" },
        ["вправо"] = new[] { "→" },
    };

    /// <summary>Разбор строки вроде «W, A, S, D, пробел, ЛКМ» в канонические клавиши.</summary>
    public static List<string> ParseKeys(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var separators = new[] { ',', ';', '|', ' ' };
        foreach (var raw in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;

            if (Aliases.TryGetValue(token, out var mapped))
            {
                foreach (var key in mapped)
                    if (!result.Contains(key)) result.Add(key);

                continue;
            }

            if (token is "↑" or "↓" or "←" or "→")
            {
                if (!result.Contains(token)) result.Add(token);
                continue;
            }

            // Одиночная буква или цифра — как есть.
            if (token.Length == 1 && char.IsLetterOrDigit(token[0]))
            {
                var key = token.ToUpperInvariant();
                if (!result.Contains(key)) result.Add(key);
            }
        }

        return result;
    }

    /// <summary>Режет список клавиш на ряды для отрисовки.</summary>
    private static string[][] Chunk(List<string> keys, int perRow)
    {
        var rows = new List<string[]>();

        for (int i = 0; i < keys.Count; i += perRow)
            rows.Add(keys.Skip(i).Take(perRow).ToArray());

        return rows.ToArray();
    }

    /// <summary>Короткая подпись клавиши для отрисовки на колпачке.</summary>
    public static string Label(string key) => key switch
    {
        "SHIFT" => "Shift",
        "SPACE" => "Пробел",
        "CTRL" => "Ctrl",
        "ESC" => "Esc",
        "TAB" => "Tab",
        "ЛКМ" => "ЛКМ",
        "ПКМ" => "ПКМ",
        "КОЛЕСО" => "Колесо",
        _ => key,
    };

    /// <summary>Широкая ли клавиша (пробел, Shift, мышь) — для отрисовки.</summary>
    public static bool IsWide(string key) => key is "SPACE" or "SHIFT" or "CTRL" or "КОЛЕСО" or "ESC";

    private static bool Contains(string text, string[] words) =>
        words.Any(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
}
