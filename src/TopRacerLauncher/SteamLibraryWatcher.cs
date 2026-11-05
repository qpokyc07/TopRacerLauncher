using System.IO;

namespace TopRacerLauncher;

/// <summary>
/// Фоновый наблюдатель за библиотекой Steam: как только в steamapps появляется
/// новый appmanifest_*.acf (игру установили), карточка сама добавляется в лаунчер
/// вместе с обложкой и метаданными.
/// </summary>
public static class SteamLibraryWatcher
{
    /// <summary>Библиотека пополнилась: (сколько добавили всего за проход).</summary>
    public static event Action<int>? LibraryGrown;

    private static readonly List<FileSystemWatcher> Watchers = new();
    private static System.Threading.Timer? _debounce;
    private static System.Threading.Timer? _rescan;
    private static int _busy;

    /// <summary>Служебные «игры» Steam — их в библиотеку не тащим.</summary>
    private static readonly string[] NonGameTitles =
    {
        "steamworks common redistributables", "steam linux runtime", "proton",
        "steamvr", "steam audio", "steam play",
    };

    /// <summary>Запускает наблюдение (если включено в настройках) и сразу догоняет пропущенное.</summary>
    public static void Start()
    {
        if (!App.Settings.AutoAddSteamGames) return;
        if (Watchers.Count == 0) AttachWatchers();

        // Игры могли установить, пока лаунчер был выключен.
        _ = Task.Run(() => ScanAsync());

        // Страховка: если событие файловой системы пропущено (или exe появился позже) — догоняем.
        _rescan ??= new System.Threading.Timer(_ => _ = ScanAsync(), null,
            TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(3));
    }

    public static void Stop()
    {
        foreach (var watcher in Watchers)
        {
            try { watcher.EnableRaisingEvents = false; watcher.Dispose(); } catch { /* уже закрыт */ }
        }

        Watchers.Clear();
        _rescan?.Dispose();
        _rescan = null;
    }

    /// <summary>Перечитать список папок Steam (например, после добавления нового диска).</summary>
    public static void Restart()
    {
        Stop();
        Start();
    }

    private static void AttachWatchers()
    {
        foreach (var steamApps in GameScanner.SteamAppsFolders())
        {
            try
            {
                var watcher = new FileSystemWatcher(steamApps, "appmanifest_*.acf")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true,
                };

                watcher.Created += (_, _) => Schedule();
                watcher.Changed += (_, _) => Schedule();
                watcher.Renamed += (_, _) => Schedule();
                watcher.Error += (_, _) => Schedule();

                Watchers.Add(watcher);
            }
            catch { /* папка недоступна — пропускаем */ }
        }
    }

    /// <summary>Steam пишет манифест несколько раз за установку — ждём паузу и сканируем один раз.</summary>
    private static void Schedule()
    {
        _debounce ??= new System.Threading.Timer(_ => _ = ScanAsync(), null, Timeout.Infinite, Timeout.Infinite);
        try { _debounce.Change(4000, Timeout.Infinite); } catch { /* таймер уже закрыт */ }
    }

    /// <summary>Добавляет в библиотеку игры Steam, которых там ещё нет. Возвращает количество новых.</summary>
    public static async Task<int> ScanAsync()
    {
        if (!App.Settings.AutoAddSteamGames) return 0;

        var found = GameScanner.ScanSteam();
        return await AddFoundAsync(found);
    }

    /// <summary>
    /// Полный поиск игр (Steam, Epic, GOG, ярлыки) — для галочки «искать новые игры при запуске».
    /// Возвращает количество добавленных.
    /// </summary>
    public static async Task<int> ScanAllAsync()
    {
        if (!App.Settings.AutoScanGames) return 0;

        var found = GameScanner.Scan(null);
        return await AddFoundAsync(found);
    }

    /// <summary>Добавляет в библиотеку то, чего в ней ещё нет, и подтягивает обложки.</summary>
    public static async Task<int> AddFoundAsync(List<ScanResult> found)
    {
        if (found.Count == 0) return 0;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return 0;

        try
        {
            var added = new List<GameItem>();

            // Настройки трогаем только в UI-потоке: там же их читает окно.
            OnUi(() =>
            {
                foreach (var candidate in found)
                {
                    if (IsServiceApp(candidate.Title)) continue;
                    if (AlreadyInLibrary(candidate)) continue;

                    var game = new GameItem
                    {
                        Title = candidate.Title,
                        ExecutablePath = candidate.FilePath,
                        AppId = candidate.AppId,
                    };

                    App.Settings.Games.Add(game);
                    added.Add(game);
                }

                if (added.Count > 0) App.SaveState();
            });

            if (added.Count == 0) return 0;

            LibraryGrown?.Invoke(added.Count);

            // Обложки грузятся из сети: без интернета скан не должен падать.
            try { await LoadArtAsync(added); } catch { /* нет сети — карточки останутся с заглушками */ }

            LibraryGrown?.Invoke(added.Count);
            return added.Count;
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    internal static bool AlreadyInLibrary(ScanResult candidate)
    {
        var removed = App.Settings.RemovedSteamIds;
        if (!string.IsNullOrWhiteSpace(candidate.AppId) && removed.Contains(candidate.AppId))
            return true;

        foreach (var game in App.Settings.Games)
        {
            if (!string.IsNullOrWhiteSpace(candidate.AppId) &&
                string.Equals(game.AppId, candidate.AppId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(game.ExecutablePath, candidate.FilePath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsServiceApp(string title)
    {
        var lower = " " + (title ?? "").ToLowerInvariant() + " ";
        return NonGameTitles.Any(name => lower.Contains(name, StringComparison.Ordinal));
    }

    /// <summary>Обложка, фон и метаданные для новых карточек.</summary>
    private static async Task LoadArtAsync(List<GameItem> games)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        foreach (var game in games)
        {
            try
            {
                var art = await ImageSearchService.FetchArtAsync(game.Title, game.AppId, cts.Token);
                if (art != null)
                {
                    if (art.CoverPath != null) game.CoverPath = art.CoverPath;
                    if (art.BackgroundPath != null) game.BackgroundPath = art.BackgroundPath;
                    if (string.IsNullOrEmpty(game.AppId)) game.AppId = art.AppId;
                }

                if (string.IsNullOrEmpty(game.Description))
                {
                    var meta = await SteamMetadataService.FetchAsync(game.AppId, game.Title, cts.Token);
                    if (meta != null) SteamMetadataService.Apply(game, meta);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* одна игра не нашлась — идём дальше */ }

            OnUi(() => App.SaveState());
        }
    }

    /// <summary>Выполняет действие в UI-потоке (если он есть), иначе сразу в текущем.</summary>
    private static void OnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) { action(); return; }

        if (dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    /// <summary>Запоминаем игру, которую админ удалил: больше не добавляем её сама.</summary>
    public static void Forget(GameItem game)
    {
        if (string.IsNullOrWhiteSpace(game.AppId)) return;

        var list = App.Settings.RemovedSteamIds;
        if (list.Contains(game.AppId)) return;

        list.Add(game.AppId);
        if (list.Count > 300) list.RemoveRange(0, list.Count - 300);
    }
}
