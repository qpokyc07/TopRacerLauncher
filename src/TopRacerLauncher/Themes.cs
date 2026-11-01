using System.Windows.Media;

namespace TopRacerLauncher;

/// <summary>Одна тема оформления (акцент + оттенок фонов).</summary>
public sealed record ThemeInfo(
    string Key,
    string Name,
    string Accent,
    string AccentHover,
    string AccentPressed,
    string BgWindow,
    string BgPanel,
    string BgCard,
    string BgCardHover);

/// <summary>Палитра тем приложения.</summary>
public static class Themes
{
    // Фоны заданы с прозрачностью (ARGB): панели и карточки получаются «стеклянными».
    public static readonly ThemeInfo[] All =
    {
        new("blue",   "Синяя",
            "#3B82D6", "#559BE5", "#2F6CB5",
            "#FF14141A", "#E61E1E26", "#CC24242E", "#E62E2E3A"),

        new("red",    "Красная",
            "#D6453B", "#E56A5F", "#B23A32",
            "#FF181416", "#E6241E20", "#CC2A2226", "#E6342A2E"),

        new("green",  "Зелёная",
            "#3FA66A", "#5BC289", "#2F8452",
            "#FF121A16", "#E61C2420", "#CC212A25", "#E6293330"),

        new("purple", "Фиолетовая",
            "#8B5CF6", "#A17CFF", "#7048D0",
            "#FF15141C", "#E6201E28", "#CC262331", "#E62E2A3A"),

        new("orange", "Оранжевая",
            "#E8863B", "#F29E5A", "#C56E2C",
            "#FF181511", "#E624211C", "#CC2A2620", "#E6332E27"),

        new("pink",   "Розовая",
            "#D6588F", "#E577A8", "#B23F72",
            "#FF19141A", "#E6241E24", "#CC2A222A", "#E6332933"),

        new("teal",   "Бирюзовая",
            "#2BB5B5", "#45CFCF", "#219494",
            "#FF121A1A", "#E61C2424", "#CC212A2A", "#E6293333"),

        new("mint",   "Мятная",
            "#3FC29A", "#5BD6AE", "#2E9A79",
            "#FF111917", "#E61A2420", "#CC202B27", "#E627332E"),

        new("cyan",   "Голубая",
            "#22A9D6", "#3FC2E8", "#1A87AD",
            "#FF111A1E", "#E61A2429", "#CC202B30", "#E627343A"),

        new("indigo", "Индиго",
            "#6366F1", "#818CF8", "#4F52D9",
            "#FF13131F", "#E61C1C29", "#CC222232", "#E62A2A3B"),

        new("amber",  "Янтарная",
            "#F0A93B", "#F7BC5E", "#CC8C26",
            "#FF191611", "#E623201A", "#CC29251E", "#E6332D26"),

        new("crimson", "Малиновая",
            "#C2455E", "#D95F76", "#A3374C",
            "#FF191416", "#E6241C1F", "#CC2A2125", "#E6332A2E"),

        new("graphite", "Графит",
            "#8A94A6", "#A3ACBC", "#6F7889",
            "#FF15181B", "#E61E2125", "#CC24282D", "#E62C3036"),
    };

    public static ThemeInfo Find(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>Применяет тему ко всему приложению (живое переключение).</summary>
    public static void Apply(string key)
    {
        var t = Find(key);
        var app = System.Windows.Application.Current;
        if (app == null) return;

        SetBrush(app, "AccentBrush", t.Accent);
        SetBrush(app, "AccentHoverBrush", t.AccentHover);
        SetBrush(app, "AccentPressedBrush", t.AccentPressed);
        SetBrush(app, "BgWindowBrush", t.BgWindow);
        SetBrush(app, "BgPanelBrush", t.BgPanel);
        SetBrush(app, "BgCardBrush", t.BgCard);
        SetBrush(app, "BgCardHoverBrush", t.BgCardHover);
    }

    private static void SetBrush(System.Windows.Application app, string resourceKey, string colorHex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(colorHex) is not Color c) return;
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            app.Resources[resourceKey] = brush;
        }
        catch { /* некорректный цвет — оставляем прежний */ }
    }
}

/// <summary>Размеры карточек (иконок) в библиотеке.</summary>
public static class CardSizes
{
    public static readonly (string Key, string Label, double Width)[] All =
    {
        ("S",  "S",  128),
        ("M",  "M",  178),
        ("L",  "L",  230),
        ("XL", "XL", 286),
    };

    public static (string Key, string Label, double Width) Find(string? key)
    {
        foreach (var s in All)
            if (string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)) return s;
        return All[1];
    }
}

/// <summary>Пропорции карточек игр: высота / ширина.</summary>
public static class CardAspects
{
    public static readonly (string Key, string Label, double Ratio)[] All =
    {
        ("3:4",  "Портрет 3:4", 4.0 / 3.0),
        ("1:1",  "Квадрат 1:1", 1.0),
        ("16:9", "Широкий 16:9", 9.0 / 16.0),
        ("2:3",  "Постер 2:3", 3.0 / 2.0),
    };

    public static (string Key, string Label, double Ratio) Find(string? key)
    {
        foreach (var a in All)
            if (string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase)) return a;
        return All[0];
    }
}
