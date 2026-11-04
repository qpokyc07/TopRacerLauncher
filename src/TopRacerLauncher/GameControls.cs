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

    /// <summary>Подсказка для игры по названию: жанр угадываем по ключевым словам.</summary>
    public static Hint For(string? title)
    {
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

    /// <summary>Клавиши, которые используются в игре (для подсветки на оверлее).</summary>
    public static IReadOnlySet<string> UsedKeys(string? title)
    {
        var hint = For(title);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in hint.Rows)
            foreach (var key in row)
                set.Add(key);

        return set;
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
