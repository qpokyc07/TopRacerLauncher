using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TopRacerLauncher;

/// <summary>Игра в библиотеке: просто ярлык с обложкой.</summary>
public sealed class GameItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";

    /// <summary>Путь к .exe/.lnk/.bat или URL вида steam://rungameid/…</summary>
    public string ExecutablePath { get; set; } = "";

    public string Arguments { get; set; } = "";

    /// <summary>Steam appid, если известен: по нему подтягиваются обложка и фон.</summary>
    public string AppId { get; set; } = "";

    /// <summary>Клавиши для оверлея, заданные вручную: «W, A, S, D, пробел, ЛКМ». Пусто — по жанру.</summary>
    public string Controls { get; set; } = "";

    public string? CoverPath { get; set; }

    /// <summary>Задний фон карточки игры (широкая картинка).</summary>
    public string? BackgroundPath { get; set; }

    // ---- Метаданные из Steam (заполняются по кнопке «Метаданные») ----

    public string? Description { get; set; }
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public string? Genres { get; set; }

    /// <summary>Год выхода (например, «2020»).</summary>
    public string? ReleaseYear { get; set; }

    public int? Metacritic { get; set; }

    /// <summary>Игра заблокирована администратором — запускать нельзя.</summary>
    public bool Locked { get; set; }
}

/// <summary>Настройки лаунчера (хранятся в %APPDATA%\TopRacerLauncher\config.json).</summary>
public sealed class Settings
{
    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    /// <summary>Версия алгоритма хэша: 2 = PBKDF2-SHA256, 0/1 = старый SHA-256.</summary>
    public int HashVersion { get; set; }

    /// <summary>Число итераций PBKDF2, с которым посчитан PasswordHash.</summary>
    public int HashIterations { get; set; }

    public int SessionMinutes { get; set; } = 60;

    /// <summary>Доступ ограничен до ввода пароля.</summary>
    public bool Locked { get; set; }

    /// <summary>Последнее известное значение остатка (для UI и совместимости).</summary>
    public int RemainingSeconds { get; set; }

    /// <summary>Абсолютный дедлайн сеанса (UTC). Основной источник правды по времени.</summary>
    public DateTime SessionEndUtc { get; set; }

    /// <summary>Остаток на момент постановки на паузу.</summary>
    public int PausedRemainingSeconds { get; set; }

    /// <summary>Таймер поставлен на паузу (отсчёт остановлен).</summary>
    public bool TimerPaused { get; set; }

    public bool SessionEverStarted { get; set; }
    public DateTime SavedAtUtc { get; set; } = DateTime.UtcNow;
    public List<GameItem> Games { get; set; } = new();

    /// <summary>Ключ текущей темы оформления (см. Themes.All).</summary>
    public string AccentTheme { get; set; } = "blue";

    /// <summary>Размер карточек (иконок) в библиотеке: ширина в пикселях (100–380).</summary>
    public double CardWidth { get; set; }

    /// <summary>Пропорции карточек игр: ключ из CardAspects.All.</summary>
    public string CardAspect { get; set; } = "3:4";

    /// <summary>Старый формат размера (S/M/L/XL) — для миграции предыдущих версий.</summary>
    public string CardSize { get; set; } = "M";

    /// <summary>Путь к фону всего приложения (картинка за библиотекой игр).</summary>
    public string? AppBackgroundPath { get; set; }

    /// <summary>Звуковые сигналы: неверный пароль, предупреждения о времени.</summary>
    public bool SoundAlerts { get; set; } = true;

    /// <summary>Подтягивать обложки из Steam автоматически при добавлении игр.</summary>
    public bool AutoCovers { get; set; } = true;

    /// <summary>Скрывать окна Steam/Rockstar/Epic, когда игра запускается через них.</summary>
    public bool HideStoreWindows { get; set; } = true;

    /// <summary>Открывать библиотеку на весь экран (без рамки Windows).</summary>
    public bool FullScreen { get; set; } = true;

    /// <summary>Ограничение по времени включено. Выключено — таймера нет и блокировки нет.</summary>
    public bool TimerEnabled { get; set; }

    /// <summary>Имя пользователя в лаунчере (видно друзьям в сети).</summary>
    public string UserName { get; set; } = "";

    /// <summary>Киоск-режим: не выпускать посетителя из лаунчера.</summary>
    public bool KioskMode { get; set; }

    /// <summary>Мой статус: online / away / dnd.</summary>
    public string MyStatus { get; set; } = "online";

    /// <summary>Полупрозрачные панели «Друзья» и «Описание».</summary>
    public bool TransparentPanels { get; set; }

    /// <summary>Названия игр цветом акцента темы.</summary>
    public bool AccentTitles { get; set; }

    /// <summary>Размер окна чата (0 — по умолчанию).</summary>
    public double ChatWidth { get; set; }
    public double ChatHeight { get; set; }

    /// <summary>Блок «Характеристики ПК» в правой панели главного экрана.</summary>
    public bool ShowPcSpecs { get; set; } = true;

    // ---- Реклама на экране приветствия ----

    /// <summary>Показывать блок рекламы (посты из соцсетей) на экране приветствия.</summary>
    public bool WelcomeAdEnabled { get; set; } = true;

    /// <summary>Заголовок рекламного блока на экране приветствия.</summary>
    public string WelcomeAdTitle { get; set; } = "Реклама постов из соцсетей";

    /// <summary>Текст рекламного блока: пост или анонс из соцсетей клуба.</summary>
    public string WelcomeAdText { get; set; } =
        "Посты из Telegram, ВКонтакте и MAX — прямо на экране клуба: " +
        "анонсы турниров, розыгрыши минут и призов. Сканируйте QR ниже, чтобы читать первыми.";

    // ---- Афиша клуба ----

    /// <summary>Показывать полосу афиши на главном экране.</summary>
    public bool AfishaEnabled { get; set; } = true;

    /// <summary>Показывать анонс турнира в верхней панели (под шапкой).</summary>
    public bool AfishaTickerEnabled { get; set; }

    /// <summary>Текст анонса в верхней панели.</summary>
    public string AfishaTickerText { get; set; } = "";

    /// <summary>События афиши: фото + заголовок + текст + дата.</summary>
    public List<AfishaItem> Afisha { get; set; } = new();

    /// <summary>Промокоды на дополнительные минуты.</summary>
    public List<PromoCode> PromoCodes { get; set; } = new();

    /// <summary>Уже использованные промокоды (чтобы один код не сработал дважды).</summary>
    public List<string> RedeemedPromos { get; set; } = new();

    /// <summary>Откуда брать обновления: URL на update.json или путь к папке с ним.</summary>
    public string UpdateUrl { get; set; } = DefaultUpdateUrl;

    /// <summary>Релизы GitHub: всегда проверяем манифест последнего релиза.</summary>
    public const string DefaultUpdateUrl =
        "https://github.com/qpokyc07/TopRacerLauncher/releases/latest/download/update.json";

    // ---- Голосовой чат ----

    /// <summary>Название микрофона (пусто — системный по умолчанию).</summary>
    public string VoiceInputDevice { get; set; } = "";

    /// <summary>Название устройства вывода (пусто — системное по умолчанию).</summary>
    public string VoiceOutputDevice { get; set; } = "";

    /// <summary>Режим передачи: "ptt" — по кнопке, "open" — постоянно.</summary>
    public string VoiceMode { get; set; } = "ptt";

    /// <summary>Виртуальный код клавиши Push-to-Talk (по умолчанию V = 0x56).</summary>
    public int VoiceKey { get; set; } = 0x56;

    /// <summary>Усиление своего микрофона, проценты (100 — как есть).</summary>
    public int VoiceInputGain { get; set; } = 100;

    /// <summary>Оверлей звонка поверх игры (кто говорит).</summary>
    public bool VoiceOverlay { get; set; } = true;

    /// <summary>Сам добавлять новые игры из Steam в библиотеку (фоновый watcher appmanifest).</summary>
    public bool AutoAddSteamGames { get; set; } = true;

    /// <summary>Искать новые игры при запуске: Steam, Epic, GOG и ярлыки на рабочем столе.</summary>
    public bool AutoScanGames { get; set; }

    /// <summary>Показывать поверх игры клавиатуру с нужными клавишами (по умолчанию выключено).</summary>
    public bool ControlsOverlay { get; set; }

    /// <summary>Разовая миграция: оверлей в игре выключен, остаётся памятка перед запуском.</summary>
    public bool OverlayOffApplied { get; set; }

    /// <summary>Свой фон: цвет верха окна (#RRGGBB). Пусто — берётся из темы.</summary>
    public string BgTopColor { get; set; } = "";

    /// <summary>Свой фон: цвет низа окна (#RRGGBB). Пусто — берётся из темы.</summary>
    public string BgBottomColor { get; set; } = "";

    /// <summary>Свой узор поверх фона: none, stripes, diag, dots, grid, rings. Пусто — из темы.</summary>
    public string BgPattern { get; set; } = "";

    /// <summary>Разовая миграция: таймер по умолчанию выключен.</summary>
    public bool TimerOffApplied { get; set; }

    /// <summary>Мастер первого запуска уже показан.</summary>
    public bool SetupDone { get; set; }

    /// <summary>Где искать обложки игр, если в Steam их нет (адрес поиска картинок).</summary>
    public string CoverSearchUrl { get; set; } =
        "https://yandex.ru/images/search?lr=64&source=serp&stype=image&text=";

    /// <summary>Показывать достижения (тосты и окно).</summary>
    public bool AchievementsEnabled { get; set; } = true;

    /// <summary>Размер оверлея управления, проценты (60–140).</summary>
    public int OverlayScale { get; set; } = 100;

    /// <summary>Положение оверлея: bottom, left, right.</summary>
    public string OverlayPosition { get; set; } = "bottom";

    /// <summary>Appid, которые админ удалил из библиотеки: заново не добавляем.</summary>
    public List<string> RemovedSteamIds { get; set; } = new();


    [System.Text.Json.Serialization.JsonIgnore]
    public bool PasswordSet => !string.IsNullOrEmpty(PasswordHash);
}

/// <summary>Событие в афише клуба: фото + заголовок + текст + дата.</summary>
public sealed class AfishaItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "";

    /// <summary>Текст события (показывается на карточке афиши).</summary>
    public string Text { get; set; } = "";

    /// <summary>Картинка события (абсолютный путь; локальные копии живут в media).</summary>
    public string? ImagePath { get; set; }

    /// <summary>Дата/время свободным текстом: «12 ноября, 19:00».</summary>
    public string DateText { get; set; } = "";

    /// <summary>Скрыть событие, не удаляя его.</summary>
    public bool Enabled { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasImage => !string.IsNullOrWhiteSpace(ImagePath) && File.Exists(ImagePath);
}

/// <summary>Карточка афиши на главном экране (фото + заголовок + текст + дата).</summary>
public sealed class AfishaCardVm
{
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    public string DateText { get; init; } = "";
    public ImageSource? Image { get; init; }

    public bool HasImage => Image != null;
    public bool HasNoImage => Image == null;
    public bool HasDate => !string.IsNullOrWhiteSpace(DateText);
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>Модель карточки игры для UI.</summary>
public sealed class GameViewModel : INotifyPropertyChanged
{
    private bool _isRunning;
    private System.Windows.Point _highlight = new(0.5, 0.4);

    public GameItem Item { get; }

    public GameViewModel(GameItem item, int playSeconds = 0)
    {
        Item = item;
        Title = string.IsNullOrWhiteSpace(item.Title) ? "Без названия" : item.Title.Trim();
        Initial = Title.Substring(0, 1).ToUpperInvariant();

        // Уменьшенное декодирование: 50 игр с фонами 1920×1080 больше не съедают сотни мегабайт.
        CoverImage = ImageCache.Load(item.CoverPath, 420);
        BackgroundImage = ImageCache.Load(item.BackgroundPath, 900);

        HasCover = CoverImage != null;
        HasBackground = BackgroundImage != null;
        ShowPlaceholder = !HasCover && !HasBackground;
        ShowInitial = !HasCover;
        IsHidden = item.Locked;
        PlaceholderBrush = MakePlaceholderBrush(Title);

        PlaySeconds = Math.Max(0, playSeconds);
        PlayTimeText = FormatPlayTime(PlaySeconds);
    }

    /// <summary>Положение блика на обложке (0..1) — следует за курсором.</summary>
    public System.Windows.Point HighlightCenter
    {
        get => _highlight;
        set
        {
            var rounded = new System.Windows.Point(Math.Round(value.X, 2), Math.Round(value.Y, 2));
            if (rounded == _highlight) return;

            _highlight = rounded;
            OnPropertyChanged();
        }
    }

    /// <summary>Сколько наиграно, секунды (0 — ещё не играли).</summary>
    public int PlaySeconds { get; }

    /// <summary>Подпись под названием: «12 ч 34 мин».</summary>
    public string PlayTimeText { get; }

    public bool HasPlayTime => PlaySeconds > 0;

    private static string FormatPlayTime(int seconds)
    {
        if (seconds <= 0) return "";

        var time = TimeSpan.FromSeconds(seconds);
        if (time.TotalHours >= 1) return $"{(int)time.TotalHours} ч {time.Minutes:D2} мин";

        return Math.Max(1, (int)time.TotalMinutes) + " мин";
    }

    public string Title { get; }
    public string Initial { get; }
    public ImageSource? CoverImage { get; }
    public ImageSource? BackgroundImage { get; }

    /// <summary>Есть ли обложка.</summary>
    public bool HasCover { get; }

    /// <summary>Есть ли задний фон.</summary>
    public bool HasBackground { get; }

    /// <summary>Рисовать ли цветную заглушку (нет ни обложки, ни фона).</summary>
    public bool ShowPlaceholder { get; }

    /// <summary>Игра скрыта от обычного режима (видна только в админ-режиме).</summary>
    public bool IsHidden { get; }

    /// <summary>Цвет названия: акцент темы или обычный текст (настройка).</summary>
    public System.Windows.Media.Brush TitleForeground =>
        App.Settings.AccentTitles
            ? (System.Windows.Application.Current?.TryFindResource("AccentBrush") as System.Windows.Media.Brush
               ?? System.Windows.Media.Brushes.White)
            : (System.Windows.Application.Current?.TryFindResource("TextBrush") as System.Windows.Media.Brush
               ?? System.Windows.Media.Brushes.White);

    /// <summary>Показывать ли букву-инициал (нет обложки).</summary>
    public bool ShowInitial { get; }

    public Brush PlaceholderBrush { get; }

    public bool IsRunning
    {
        get => _isRunning;
        set { _isRunning = value; OnPropertyChanged(); }
    }

    private static Brush MakePlaceholderBrush(string title)
    {
        // Детерминированный цвет-заглушка из названия игры.
        int hash = 0;
        foreach (var ch in title) hash = unchecked(hash * 31 + char.ToLowerInvariant(ch));
        hash &= 0x7FFFFFFF;   // без Math.Abs: тот падает на int.MinValue
        var palette = new[]
        {
            (Color)ColorConverter.ConvertFromString("#2F4A7A")!,
            (Color)ColorConverter.ConvertFromString("#5A3A7A")!,
            (Color)ColorConverter.ConvertFromString("#2F6A5A")!,
            (Color)ColorConverter.ConvertFromString("#7A4A2F")!,
            (Color)ColorConverter.ConvertFromString("#3A5A7A")!,
            (Color)ColorConverter.ConvertFromString("#6A2F4A")!,
            (Color)ColorConverter.ConvertFromString("#4A6A2F")!,
            (Color)ColorConverter.ConvertFromString("#44506B")!,
        };
        var c = palette[hash % palette.Length];
        var grad = new LinearGradientBrush(c, ControlPaintDark(c), 45d);
        grad.Freeze();
        return grad;
    }

    private static Color ControlPaintDark(Color c) =>
        Color.FromRgb((byte)(c.R * 0.55), (byte)(c.G * 0.55), (byte)(c.B * 0.55));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Коллекция игр, безопасная для привязки.</summary>
public sealed class Library
{
    public ObservableCollection<GameViewModel> Items { get; } = new();
}
