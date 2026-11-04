using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TopRacerLauncher;

/// <summary>Найденная при скане игра.</summary>
public sealed record ScanResult(string Title, string FilePath, string Source, string Arguments = "", string AppId = "");

/// <summary>
/// Скан установленных игр: Steam, Epic, GOG и ярлыки (Рабочий стол, «Пуск»).
/// </summary>
public static class GameScanner
{
    private static readonly string[] SkipFolders =
    {
        "_commonredist", "redist", "redistributable", "directx", "directxsetup",
        "_installer", "installer", "dotnet", "prerequisites", "support",
    };

    /// <summary>Куски имён exe, которые почти никогда не являются самой игрой.</summary>
    private static readonly string[] SkipExeParts =
    {
        "unins", "uninstall", "setup", "vcredist", "dxsetup", "dxwebsetup",
        "dotnet", "crash", "report", "updater", "redist", "installer",
        "vconsole", "hlds", "srcds", "steamerrorreporter",
    };

    /// <summary>Исполняемые файлы магазинов: ярлык на них не даёт понять, какая это игра.</summary>
    private static readonly string[] LauncherExes =
    {
        "steam", "steamwebhelper", "epicgameslauncher", "galaxyclient", "goggalaxy",
        "origin", "eadesktop", "uplay", "ubisoftconnect", "battle.net", "riotclientservices",
        "riotclient", "upc", "playnite", "itch",
    };

    public static List<ScanResult> Scan(Action<string>? report)
    {
        var results = new List<ScanResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        report?.Invoke("Поиск Steam…");
        TryScanSteam(results, seen);

        report?.Invoke("Поиск Epic Games и GOG…");
        TryScanGameFolders(results, seen, "Epic", GetEpicLibraryFolders());
        TryScanGameFolders(results, seen, "GOG", GetGogLibraryFolders());

        report?.Invoke("Поиск ярловиков (Рабочий стол, «Пуск»)…");
        TryScanShortcuts(results, seen);

        return results
            .OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    // ------------------------------------------------------------------ Steam

    /// <summary>Папки steamapps всех библиотек Steam (основная + из libraryfolders.vdf).</summary>
    public static List<string> SteamAppsFolders()
    {
        var folders = new List<string>();

        var steamPath = FindSteamPath();
        if (steamPath is null) return folders;

        folders.Add(Path.Combine(steamPath, "steamapps"));

        // Дополнительные библиотеки из libraryfolders.vdf
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(vdf))
            {
                var text = File.ReadAllText(vdf);
                foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
                {
                    var p = m.Groups[1].Value.Replace("\\\\", "\\");
                    var sa = Path.Combine(p, "steamapps");
                    if (Directory.Exists(sa) && !folders.Contains(sa, StringComparer.OrdinalIgnoreCase))
                        folders.Add(sa);
                }
            }
        }
        catch { /* повреждённый vdf — работаем с основной библиотекой */ }

        return folders.Where(Directory.Exists).ToList();
    }

    /// <summary>Игры только из Steam — для фонового автодополнения библиотеки.</summary>
    public static List<ScanResult> ScanSteam()
    {
        var results = new List<ScanResult>();
        TryScanSteam(results, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        return results;
    }

    private static void TryScanSteam(List<ScanResult> results, HashSet<string> seen)
    {
        var steamAppsList = SteamAppsFolders();
        if (steamAppsList.Count == 0) return;

        foreach (var steamApps in steamAppsList)
        {
            string[] acfFiles;
            try { acfFiles = Directory.GetFiles(steamApps, "appmanifest_*.acf"); }
            catch { continue; }

            foreach (var acf in acfFiles)
            {
                try
                {
                    var content = File.ReadAllText(acf);

                    var name = MatchField(content, "name");
                    var installDir = MatchField(content, "installdir");
                    var appId = MatchField(content, "appid");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDir))
                        continue;

                    var gameDir = Path.Combine(steamApps, "common", installDir);
                    if (!Directory.Exists(gameDir)) continue;

                    var exe = FindGameExe(gameDir, installDir);
                    if (exe is null || !Add(results, seen, name, exe, "Steam", appId: appId)) continue;
                }
                catch { /* пропускаем проблемный манифест */ }
            }
        }
    }

    private static string? MatchField(string content, string field)
    {
        var m = Regex.Match(content, $"\"{field}\"\\s*\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? FindSteamPath()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var p = k?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(p))
            {
                p = p.Replace('/', '\\');
                if (Directory.Exists(p)) return p;
            }
        }
        catch { /* нет доступа */ }

        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            var p = k?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) return p;
        }
        catch { /* нет доступа */ }

        foreach (var cand in new[]
                 {
                     @"C:\Program Files (x86)\Steam",
                     @"C:\Program Files\Steam",
                     @"D:\Steam",
                     @"D:\Program Files (x86)\Steam",
                     @"E:\Steam",
                 })
        {
            if (Directory.Exists(cand)) return cand;
        }
        return null;
    }

    // ------------------------------------------------- Epic / GOG / папки игр

    private static IEnumerable<string> GetEpicLibraryFolders()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return new[]
        {
            Path.Combine(pf, "Epic Games"),
            Path.Combine(pf86, "Epic Games"),
        };
    }

    private static IEnumerable<string> GetGogLibraryFolders()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return new[]
        {
            Path.Combine(pf86, "GOG Galaxy", "Games"),
            Path.Combine(pf, "GOG Galaxy", "Games"),
        };
    }

    private static void TryScanGameFolders(List<ScanResult> results, HashSet<string> seen,
        string source, IEnumerable<string> roots)
    {
        foreach (var root in roots)
        {
            string[] dirs;
            try { dirs = Directory.GetDirectories(root); }
            catch { continue; } // папки нет или нет доступа

            foreach (var dir in dirs)
            {
                try
                {
                    var title = Path.GetFileName(dir);
                    var exe = FindGameExe(dir, title);
                    if (exe != null)
                        Add(results, seen, title, exe, source);
                }
                catch { /* пропускаем */ }
            }
        }
    }

    // --------------------------------------------------------------- Ярлыки

    /// <summary>
    /// Сканирует ярлыки на рабочем столе (свои и общие). «Пуск» не трогаем:
    /// оттуда в библиотеку попадали панель управления, калькулятор и прочие утилиты.
    /// </summary>
    private static void TryScanShortcuts(List<ScanResult> results, HashSet<string> seen)
    {
        var folders = new List<string>();

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

        folders.Add(desktop);
        if (!string.Equals(desktop, publicDesktop, StringComparison.OrdinalIgnoreCase))
            folders.Add(publicDesktop);

        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null) shell = Activator.CreateInstance(shellType);
        }
        catch { shell = null; }

        if (shell == null) return;

        try
        {
            foreach (var folder in folders)
            {
                string[] files;
                try
                {
                    files = Directory.GetFiles(folder, "*.lnk", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(folder, "*.url", SearchOption.AllDirectories))
                        .ToArray();
                }
                catch { continue; }

                foreach (var file in files)
                {
                    try
                    {
                        var title = Path.GetFileNameWithoutExtension(file);

                        if (string.Equals(Path.GetExtension(file), ".url", StringComparison.OrdinalIgnoreCase))
                        {
                            // Так Steam/GOG/Epic кладут ярлыки: URL=steam://rungameid/12345
                            var url = ReadUrlTarget(file);
                            if (url != null) Add(results, seen, title, url, "Ярлык (URL)", appId: AppIdFromUrl(url));
                            continue;
                        }

                        dynamic ws = shell;
                        dynamic shortcut = ws.CreateShortcut(file);
                        string target = (string)(shortcut.TargetPath ?? "");
                        string shortcutArgs = (string)(shortcut.Arguments ?? "");
                        if (string.IsNullOrWhiteSpace(target)) continue;
                        if (!string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Ярлык ведёт на магазин, а не на игру: без appid непонятно, что запускать.
                        if (IsLauncher(target)) continue;

                        // Утилиты Windows и наши собственные ярлыки играми не считаем.
                        if (IsSystemUtility(target)) continue;

                        Add(results, seen, title, target, "Ярлык", shortcutArgs);
                    }
                    catch { /* битый ярлык — пропускаем */ }
                }
            }
        }
        finally
        {
            try { System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); } catch { /* ок */ }
        }
    }

    private static bool IsLauncher(string exePath)
    {
        var name = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        return LauncherExes.Contains(name);
    }

    /// <summary>Утилиты Windows и сам лаунчер — не игры, в библиотеку не берём.</summary>
    private static bool IsSystemUtility(string exePath)
    {
        var lower = exePath.ToLowerInvariant();

        if (lower.Contains("\\windows\\")) return true;
        if (lower.EndsWith("topracerlauncher.exe", StringComparison.Ordinal)) return true;

        var name = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        return name is "deepseek harness" or "playnite.desktopapp" or "playnite.fullscreenapp"
            or "discord" or "chrome" or "msedge" or "firefox" or "yandex"
            or "obs64" or "obs32" or "cursor" or "code" or "winrar" or "itunes";
    }

    /// <summary>Достаёт appid из ссылки вида steam://rungameid/782330.</summary>
    private static string AppIdFromUrl(string url)
    {
        var m = Regex.Match(url, @"(?:rungameid|run/|app/)(\d{2,10})", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string? ReadUrlTarget(string path)
    {
        try
        {
            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();
                if (!line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) continue;

                var url = line.Substring(4).Trim();
                if (url.Length == 0) return null;

                // Берём только схемы магазинов: http(s)-ярлык нам не поможет.
                int sep = url.IndexOf("://", StringComparison.Ordinal);
                if (sep <= 0) return null;
                var scheme = url.Substring(0, sep).ToLowerInvariant();
                return scheme is "steam" or "goggalaxy" or "uplay" or "origin" or "com.epicgames.launcher"
                    ? url
                    : null;
            }
        }
        catch { /* нечитаемый файл */ }
        return null;
    }

    // -------------------------------------------------------------- Утилиты

    /// <summary>Ищет главный exe игры в её папке (имя папки → корень игры → ближе к корню → больше).</summary>
    internal static string? FindGameExe(string dir, string gameName)
    {
        var candidates = new List<(FileInfo File, int Depth)>();
        CollectExes(new DirectoryInfo(dir), 0, candidates);

        if (candidates.Count == 0) return null;

        // Отбрасываем установщики/апдейтеры/консольные утилиты: «самый большой exe» часто и есть unins000.exe.
        var usable = candidates.Where(c => !LooksLikeHelper(c.File.Name)).ToList();
        if (usable.Count == 0) usable = candidates;

        var byName = usable.FirstOrDefault(c =>
            string.Equals(Path.GetFileNameWithoutExtension(c.File.Name), gameName,
                StringComparison.OrdinalIgnoreCase));
        if (byName.File != null) return byName.File.FullName;

        // Ближе к корню игры — больше шансов, что это сама игра, а не tools/bin/x64.
        return usable
            .OrderBy(c => c.Depth)
            .ThenByDescending(c => c.File.Length)
            .FirstOrDefault().File?.FullName;
    }

    private static bool LooksLikeHelper(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        return SkipExeParts.Any(p => name.Contains(p, StringComparison.Ordinal));
    }

    private static void CollectExes(DirectoryInfo dir, int depth, List<(FileInfo File, int Depth)> output)
    {
        if (depth > 3 || output.Count >= 120) return;

        try
        {
            foreach (var file in dir.EnumerateFiles("*.exe"))
                output.Add((file, depth));
        }
        catch { /* нет доступа */ }

        if (depth == 3) return;

        IEnumerable<DirectoryInfo> subDirs;
        try { subDirs = dir.EnumerateDirectories(); }
        catch { return; }

        foreach (var sub in subDirs)
        {
            if (SkipFolders.Contains(sub.Name.ToLowerInvariant())) continue;
            CollectExes(sub, depth + 1, output);
        }
    }

    private static bool Add(List<ScanResult> results, HashSet<string> seen,
        string title, string path, string source, string arguments = "", string? appId = null)
    {
        var key = path.Trim().ToLowerInvariant();
        if (!seen.Add(key)) return false;

        results.Add(new ScanResult(title.Trim(), path, source, arguments?.Trim() ?? "", appId?.Trim() ?? ""));
        return true;
    }
}
