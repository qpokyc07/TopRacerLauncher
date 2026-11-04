using System.Windows;
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
    string BgCardHover,
    string? BgWindow2 = null,
    string? BgWindow3 = null,
    string? Pattern = null,
    string? PatternColor = null);

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
            "#E0409E", "#F466B6", "#BC2F80",
            "#FF19141A", "#E6241E24", "#CC2A222A", "#E6332933"),

        new("teal",   "Бирюзовая",
            "#2BB5B5", "#45CFCF", "#219494",
            "#FF121A1A", "#E61C2424", "#CC212A2A", "#E6293333"),

        new("mint",   "Мятная",
            "#3FC29A", "#5BD6AE", "#2E9A79",
            "#FF111917", "#E61A2420", "#CC202B27", "#E627332E"),

        new("cyan",   "Аква",
            "#00C2D1", "#2EDBEA", "#009AA6",
            "#FF111A1E", "#E61A2429", "#CC202B30", "#E627343A"),

        new("indigo", "Индиго",
            "#6366F1", "#818CF8", "#4F52D9",
            "#FF13131F", "#E61C1C29", "#CC222232", "#E62A2A3B"),

        new("amber",  "Янтарная",
            "#F0A93B", "#F7BC5E", "#CC8C26",
            "#FF191611", "#E623201A", "#CC29251E", "#E6332D26"),

        new("crimson", "Малиновая",
            "#A6144B", "#C93070", "#840F3B",
            "#FF191416", "#E6241C1F", "#CC2A2125", "#E6332A2E"),

        new("graphite", "Графит",
            "#8A94A6", "#A3ACBC", "#6F7889",
            "#FF15181B", "#E61E2125", "#CC24282D", "#E62C3036"),

        // --- дополнительные палитры ---
        new("slate",  "Сланцевая",
            "#64748B", "#7E8CA3", "#4E5A6E",
            "#FF12151A", "#E61B1F26", "#CC21262E", "#E6292F38"),

        new("emerald", "Изумруд",
            "#10B981", "#2ED3A0", "#0C9367",
            "#FF0F1A16", "#E6182320", "#CC1E2A26", "#E626332E"),

        new("rose",   "Роза",
            "#F43F5E", "#FF6480", "#D12E4C",
            "#FF1A1216", "#E6231B20", "#CC2A2127", "#E633292F"),

        new("sky",    "Небо",
            "#0EA5E9", "#38BDF8", "#0B86BE",
            "#FF101820", "#E619222A", "#CC1F2932", "#E6273239"),

        new("violet", "Виолет",
            "#9D4DFF", "#B878FF", "#7A2FD9",
            "#FF14111C", "#E61D1A28", "#CC232031", "#E62B283B"),

        new("cocoa",  "Какао",
            "#A16207", "#C07C18", "#825005",
            "#FF17130D", "#E6201B14", "#CC272119", "#E62F2820"),

        new("steel",  "Сталь",
            "#475569", "#5E6E85", "#37444F",
            "#FF121519", "#E61B1F24", "#CC21262C", "#E6292F36"),

        new("lime",   "Кислота",
            "#C3F53C", "#D6FF6B", "#9BCB1E",
            "#FF141A0F", "#E61D2415", "#CC232A1B", "#E62B3322"),

        // --- градиентные фоны (три цвета: тёмный → насыщенный → тёмный) ---
        new("sunset", "Закат (градиент)",
            "#FF7A45", "#FF9A6B", "#D95E2E",
            "#FF12060F", "#E6221830", "#CC281E3A", "#E6302545", "#FF2B0F2A", "#FF5C1A3A"),

        new("ocean",  "Океан (градиент)",
            "#2BB5D6", "#4FD0EC", "#1E93AF",
            "#FF05111E", "#E6132436", "#CC182C40", "#E61F354C", "#FF0A2A44", "#FF0E4A66"),

        new("neon",   "Неон (градиент)",
            "#C026D3", "#DC4AEC", "#9C1DAF",
            "#FF0B0514", "#E61A0F26", "#CC21132F", "#E6291A39", "#FF2A0638", "#FF55105A"),

        new("cyber",  "Кибер (градиент)",
            "#00C2A8", "#22E0C4", "#009C87",
            "#FF04100E", "#E6121E23", "#CC18252B", "#E61F2E35", "#FF0A2A26", "#FF0E4A3E"),

        new("aurora", "Сияние (градиент)",
            "#4ADE80", "#6FF09C", "#33B863",
            "#FF05130D", "#E6122426", "#CC182B2E", "#E61F3438", "#FF0C2A24", "#FF124A44"),

        new("gold",   "Золото (градиент)",
            "#D4AF37", "#E8C657", "#B08F24",
            "#FF100C05", "#E6231C0E", "#CC2A2214", "#E6332919", "#FF2A2008", "#FF4A3810"),

        // --- колоритные палитры (добавлены в 1.9.18) ---
        new("magma",  "Магма (градиент)",
            "#FF4D1F", "#FF7A50", "#D63A10",
            "#FF140503", "#E6240A06", "#CC2A1008", "#E632150B", "#FF33080A", "#FF66120E"),

        new("matrix", "Матрица (градиент)",
            "#00FF66", "#4DFF94", "#00CC52",
            "#FF030B06", "#E60A1610", "#CC101E15", "#E6162519", "#FF06200C", "#FF0C4020"),

        new("ultra",  "Ультрафиолет",
            "#A020F0", "#B84DFF", "#7F14C4",
            "#FF0F0517", "#E6190C24", "#CC1F1230", "#E6271A3C"),

        new("ice",    "Лёд",
            "#7DD3FC", "#A5E4FF", "#4FB3E0",
            "#FF0D1418", "#E6161F26", "#CC1C262E", "#E6243038"),

        new("rust",   "Ржавчина",
            "#C25A1E", "#D97A3C", "#9A4413",
            "#FF160F09", "#E61F1712", "#CC261D16", "#E62F251C"),

        new("corail", "Коралл",
            "#FF6F61", "#FF8F84", "#D95A4E",
            "#FF170F10", "#E6211819", "#CC281E1F", "#E6322728"),

        // --- двухцветные темы с узорами ---
        new("limecoal", "Лайм + уголь (косые полосы)",
            "#C3F53C", "#D6FF6B", "#9BCB1E",
            "#FF14161A", "#E61D2024", "#CC23272C", "#E62B3036",
            null, null, "diag", "#C3F53C"),

        new("magmasnow", "Магма + тёплый снег (точки)",
            "#FF5A2B", "#FF7E52", "#D94514",
            "#FF170D0B", "#E6201512", "#CC261A16", "#E62F221D",
            null, null, "dots", "#F5E6D3"),

        new("mintgraph", "Мята + графит (сетка)",
            "#43E0B0", "#6BF0C7", "#2AB88E",
            "#FF101513", "#E619201D", "#CC1F2724", "#E627302C",
            null, null, "grid", "#43E0B0"),

        new("amethyst", "Аметист + шампань (кольца)",
            "#B36BFF", "#C88DFF", "#9450DB",
            "#FF120E1A", "#E61B1626", "#CC211C2D", "#E6292337",
            null, null, "rings", "#F0DDB4"),

        new("sapphire", "Сапфир + медь (диагональ)",
            "#2F6BFF", "#5A8CFF", "#2354D9",
            "#FF0A101F", "#E6131A2B", "#CC192133", "#E6202A3F",
            null, null, "diag", "#C97B45"),

        new("neonrose", "Неон-роза + чёрный (полосы)",
            "#FF2E88", "#FF5AA4", "#D91C6E",
            "#FF120910", "#E61C0F19", "#CC23141F", "#E62B1A27",
            null, null, "stripes", "#FF2E88"),

        new("emeraldgold", "Изумруд + золото (полосы)",
            "#12C98B", "#38E3A8", "#0AA470",
            "#FF0B1512", "#E6141F1B", "#CC1A2622", "#E6212F2A",
            null, null, "stripes", "#E8C657"),

        new("cobaltcor", "Кобальт + коралл (клетка)",
            "#3E7BFA", "#6B9DFF", "#2C61D9",
            "#FF0C1120", "#E6161B2B", "#CC1C2233", "#E6242B3F",
            null, null, "grid", "#FF8A76"),
    };

    /// <summary>Узор темы: полосы, точки, сетка или кольца. null — без узора.</summary>
    public static Brush? CreatePattern(ThemeInfo theme)
    {
        if (string.IsNullOrWhiteSpace(theme.Pattern)) return null;

        var color = ColorConverter.ConvertFromString(theme.PatternColor ?? theme.Accent) is Color c
            ? c
            : Color.FromRgb(0xFF, 0xFF, 0xFF);

        var soft = Color.FromArgb(0x22, color.R, color.G, color.B);
        var faint = Color.FromArgb(0x00, color.R, color.G, color.B);

        Brush? brush = theme.Pattern switch
        {
            "diag" => Stripes(soft, faint, 26, 45),
            "stripes" => Stripes(soft, faint, 26, 90),
            "dots" => Dots(color),
            "grid" => Grid(color),
            "rings" => Rings(color),
            _ => null,
        };

        brush?.Freeze();
        return brush;
    }

    /// <summary>Повторяющиеся полосы заданной толщины и наклона.</summary>
    private static Brush Stripes(Color soft, Color faint, double tile, double angle)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new System.Windows.Point(0, 0),
            EndPoint = new System.Windows.Point(0, tile),
            SpreadMethod = GradientSpreadMethod.Repeat,
        };

        brush.GradientStops.Add(new GradientStop(soft, 0));
        brush.GradientStops.Add(new GradientStop(soft, 0.34));
        brush.GradientStops.Add(new GradientStop(faint, 0.34));
        brush.GradientStops.Add(new GradientStop(faint, 1));

        if (angle != 0)
        {
            brush.RelativeTransform = new RotateTransform(angle, 0.5, 0.5);
        }

        return brush;
    }

    /// <summary>Точки по фону.</summary>
    private static Brush Dots(Color color)
    {
        var dot = new GeometryDrawing(
            new SolidColorBrush(Color.FromArgb(0x45, color.R, color.G, color.B)),
            null,
            new EllipseGeometry(new System.Windows.Point(9, 9), 2.4, 2.4));

        var drawing = new DrawingGroup();
        drawing.Children.Add(dot);

        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new System.Windows.Rect(0, 0, 18, 18),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
    }

    /// <summary>Клетка (сетка) по фону.</summary>
    private static Brush Grid(Color color)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)), 1);

        var group = new DrawingGroup();

        var line1 = new GeometryDrawing(null, pen, new LineGeometry(new System.Windows.Point(0, 0.5), new System.Windows.Point(44, 0.5)));
        var line2 = new GeometryDrawing(null, pen, new LineGeometry(new System.Windows.Point(0.5, 0), new System.Windows.Point(0.5, 44)));

        group.Children.Add(line1);
        group.Children.Add(line2);

        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new System.Windows.Rect(0, 0, 44, 44),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
    }

    /// <summary>Концентрические кольца.</summary>
    private static Brush Rings(Color color)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x2A, color.R, color.G, color.B)), 1.2);

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new System.Windows.Point(30, 30), 10, 10)));
        group.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new System.Windows.Point(30, 30), 20, 20)));
        group.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new System.Windows.Point(30, 30), 29, 29)));

        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new System.Windows.Rect(0, 0, 60, 60),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
    }

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
        SetWindowBrush(app, t.BgWindow, t.BgWindow2, t.BgWindow3);
        SetBrush(app, "BgPanelBrush", t.BgPanel);
        SetBrush(app, "BgCardBrush", t.BgCard);
        SetBrush(app, "BgCardHoverBrush", t.BgCardHover);

        // На светлых акцентах (лайм, лёд, золото) белый текст не читается — делаем тёмный.
        var accentColor = ColorConverter.ConvertFromString(t.Accent) is Color ac ? ac : Colors.White;
        double luminance = (0.299 * accentColor.R + 0.587 * accentColor.G + 0.114 * accentColor.B) / 255.0;
        app.Resources["AccentTextBrush"] = new SolidColorBrush(
            luminance > 0.62 ? Color.FromRgb(0x14, 0x14, 0x18) : Colors.White);

        // Узор фона: у двухцветных тем поверх — полосы, точки, сетка или кольца.
        var pattern = CreatePattern(t);
        app.Resources["PatternBrush"] = pattern ?? new SolidColorBrush(Colors.Transparent);
    }

    /// <summary>Фон окна: сплошной цвет или плавный градиент (если задан второй цвет).</summary>
    private static void SetWindowBrush(System.Windows.Application app, string fromHex, string? toHex, string? thirdHex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(fromHex) is not Color from) return;

            Brush brush;

            if (!string.IsNullOrWhiteSpace(toHex) && ColorConverter.ConvertFromString(toHex) is Color to)
            {
                // Насыщенный переход: тёмный → цвет → тёмный по диагонали.
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 1),
                };

                gradient.GradientStops.Add(new GradientStop(from, 0));
                gradient.GradientStops.Add(new GradientStop(to, 0.55));

                var last = ColorConverter.ConvertFromString(
                    string.IsNullOrWhiteSpace(thirdHex) ? fromHex : thirdHex!) is Color c ? c : from;
                gradient.GradientStops.Add(new GradientStop(last, 1));

                brush = gradient;
            }
            else
            {
                brush = new SolidColorBrush(from);
            }

            brush.Freeze();
            app.Resources["BgWindowBrush"] = brush;
        }
        catch { /* некорректный цвет — оставляем прежний */ }
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
