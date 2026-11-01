using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TopRacerLauncher;

/// <summary>Результат самопроверки.</summary>
public sealed record SelfTestReport(bool Ok, string Text);

/// <summary>
/// Логические проверки для режима --smoke-test: пароль, конфиг, таймер, удаление медиа, скан.
/// Работают в отдельном временном каталоге и не трогают реальные настройки.
/// </summary>
public static class SelfTest
{
    public static SelfTestReport Run()
    {
        var lines = new List<string>();
        bool failed = false;

        void Check(string name, Func<string?> test)
        {
            try
            {
                var error = test();
                if (error == null)
                {
                    lines.Add("OK   " + name);
                }
                else
                {
                    lines.Add("FAIL " + name + " — " + error);
                    failed = true;
                }
            }
            catch (Exception ex)
            {
                lines.Add("FAIL " + name + " — исключение: " + ex.Message);
                failed = true;
            }
        }

        var previousDirectory = ConfigStore.ConfigDirectory;
        var dir = Path.Combine(Path.GetTempPath(), "TopRacerLauncherSelfTest");
        try { Directory.Delete(dir, recursive: true); } catch { /* не страшно */ }
        Directory.CreateDirectory(dir);
        ConfigStore.UseDirectory(dir);

        try
        {
            Check("Промокод: создание и погашение", () =>
        {
            var promo = PromoService.Create(15, 1, 1);
            if (!PromoService.TryRedeem(promo.Code, out int minutes, out string error))
                return "не погасился: " + error;
            if (minutes != 15) return "неверные минуты: " + minutes;

            if (PromoService.TryRedeem(promo.Code, out _, out _))
                return "повторное погашение прошло";

            App.Settings.PromoCodes.RemoveAll(p => p.Code == promo.Code);
            App.Settings.RedeemedPromos.RemoveAll(c => c == promo.Code);
            return null;
        });

        Check("PBKDF2: верный пароль принимается", () =>
            {
                var s = new Settings();
                ConfigStore.SetPassword(s, "correct horse battery");
                if (s.HashVersion != ConfigStore.CurrentHashVersion) return "версия хэша не проставилась";
                if (s.HashIterations < 100_000) return "слишком мало итераций";
                if (!ConfigStore.VerifyPassword(s, "correct horse battery")) return "верный пароль отклонён";
                if (ConfigStore.VerifyPassword(s, "wrong-password")) return "неверный пароль принят";
                return null;
            });

            Check("Миграция старого SHA-256 хэша", () =>
            {
                var salt = ConfigStore.CreateSalt();
                var legacy = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(salt + "||" + "old-pass")));
                var s = new Settings { PasswordSalt = salt, PasswordHash = legacy, HashVersion = 0 };

                if (!ConfigStore.VerifyPassword(s, "old-pass")) return "старый хэш не принят";
                if (s.HashVersion != ConfigStore.CurrentHashVersion) return "хэш не обновился до PBKDF2";
                if (!ConfigStore.VerifyPassword(s, "old-pass")) return "после миграции пароль не работает";
                return null;
            });

            Check("Сохранение и загрузка конфига", () =>
            {
                var s = new Settings { SessionMinutes = 45 };
                ConfigStore.SetPassword(s, "roundtrip-pass");
                s.SessionEndUtc = DateTime.UtcNow.AddMinutes(45);
                s.RemainingSeconds = 45 * 60;
                s.Games.Add(new GameItem { Title = "Test Game", ExecutablePath = @"C:\game.exe" });

                if (!ConfigStore.Save(s)) return "сохранение вернуло false";

                var loaded = ConfigStore.Load();
                if (ConfigStore.FatalError != null) return "ошибка загрузки: " + ConfigStore.FatalError;
                if (loaded.Games.Count != 1) return "игра не сохранилась";
                if (!ConfigStore.VerifyPassword(loaded, "roundtrip-pass")) return "пароль не сохранился";
                if (Math.Abs((loaded.SessionEndUtc - s.SessionEndUtc).TotalSeconds) > 2)
                    return "дедлайн сеанса не сохранился";
                if (File.Exists(ConfigStore.ConfigPath + ".tmp")) return "остался временный файл";
                return null;
            });

            Check("Нормализация настроек при загрузке", () =>
            {
                var s = new Settings { SessionMinutes = 999, CardWidth = 5 };
                ConfigStore.SetPassword(s, "normalize-pass");
                if (!ConfigStore.Save(s)) return "не сохранилось";

                var loaded = ConfigStore.Load();
                if (loaded.SessionMinutes != 240) return "SessionMinutes не ограничен: " + loaded.SessionMinutes;
                if (loaded.CardWidth < 100 || loaded.CardWidth > 380) return "CardWidth не ограничен: " + loaded.CardWidth;
                return null;
            });

            Check("Пропорции и режимы сохраняются", () =>
            {
                var s = new Settings
                {
                    CardAspect = "16:9",
                    FullScreen = false,
                    TimerEnabled = false,

                };
                s.Games.Add(new GameItem { Title = "A", ExecutablePath = @"C:\a.exe" });
                ConfigStore.SetPassword(s, "modes-pass");
                if (!ConfigStore.Save(s)) return "не сохранилось";

                var loaded = ConfigStore.Load();
                if (loaded.CardAspect != "16:9") return "пропорции потерялись: " + loaded.CardAspect;
                if (loaded.FullScreen) return "полный экран не сохранился";
                if (loaded.TimerEnabled) return "выключенный таймер не сохранился";

                if (loaded.Games.Count != 1) return "игра потерялась";

                if (Math.Abs(CardAspects.Find("16:9").Ratio - 9.0 / 16.0) > 0.001)
                    return "пропорция 16:9 считается неверно";
                return null;
            });

            Check("Восстановление из .bak при битом конфиге", () =>
            {
                // Готовим заведомо валидный конфиг с игрой и копируем его в .bak.
                var s = new Settings { SessionMinutes = 30 };
                ConfigStore.SetPassword(s, "backup-pass");
                s.Games.Add(new GameItem { Title = "From backup", ExecutablePath = @"C:\backup.exe" });
                if (!ConfigStore.Save(s)) return "не удалось подготовить конфиг";

                File.WriteAllText(ConfigStore.BackupPath, File.ReadAllText(ConfigStore.ConfigPath));
                File.WriteAllText(ConfigStore.ConfigPath, "{ это не json ");

                var recovered = ConfigStore.Load();
                if (ConfigStore.FatalError != null) return "вместо восстановления — фатальная ошибка";
                if (ConfigStore.Warning == null) return "нет предупреждения о восстановлении";
                if (recovered.Games.Count != 1) return "данные из резервной копии не поднялись";

                var quarantined = Directory.GetFiles(dir, "config.json.corrupt-*");
                if (quarantined.Length == 0) return "битый файл не сохранён рядом";
                return null;
            });

            Check("Битый конфиг без бэкапа не затирается", () =>
            {
                try { File.Delete(ConfigStore.BackupPath); } catch { /* могло не быть */ }
                File.WriteAllText(ConfigStore.ConfigPath, "garbage");

                var empty = ConfigStore.Load();
                if (ConfigStore.FatalError == null) return "не сообщил о неустранимой ошибке";
                if (ConfigStore.Save(empty)) return "сохранение затёрло повреждённый конфиг";
                return null;
            });

            Check("Удаление картинок строго внутри media", () =>
            {
                var media = ImageSearchService.MediaDirectory;
                Directory.CreateDirectory(media);
                var inside = Path.Combine(media, "inside.jpg");
                File.WriteAllText(inside, "x");

                var neighbour = Path.Combine(Path.GetDirectoryName(media)!, "media2");
                Directory.CreateDirectory(neighbour);
                var outside = Path.Combine(neighbour, "outside.jpg");
                File.WriteAllText(outside, "x");

                ImageSearchService.DeleteIfMedia(inside);
                if (File.Exists(inside)) return "файл внутри media не удалён";

                ImageSearchService.DeleteIfMedia(outside);
                if (!File.Exists(outside)) return "удалён файл вне media (проверка префикса)";
                return null;
            });

            Check("Расчёт остатка времени по дедлайну", () =>
            {
                var s = new Settings { SessionEndUtc = DateTime.UtcNow.AddSeconds(90) };
                int remaining = App.ComputeRemainingSeconds(s);
                if (remaining < 88 || remaining > 92) return "остаток " + remaining;

                var paused = new Settings
                {
                    TimerPaused = true,
                    PausedRemainingSeconds = 42,
                    SessionEndUtc = DateTime.UtcNow.AddSeconds(90),
                };
                if (App.ComputeRemainingSeconds(paused) != 42) return "пауза не учитывается";

                var expired = new Settings { SessionEndUtc = DateTime.UtcNow.AddSeconds(-5) };
                if (App.ComputeRemainingSeconds(expired) != 0) return "просроченный сеанс не обнулился";
                return null;
            });

            Check("Выбор главного exe игры", () =>
            {
                var gameDir = Path.Combine(dir, "TestGame");
                Directory.CreateDirectory(gameDir);
                File.WriteAllBytes(Path.Combine(gameDir, "unins000.exe"), new byte[400]);
                File.WriteAllBytes(Path.Combine(gameDir, "helper.exe"), new byte[300]);
                File.WriteAllBytes(Path.Combine(gameDir, "TestGame.exe"), new byte[100]);

                var picked = GameScanner.FindGameExe(gameDir, "TestGame");
                if (picked == null) return "exe не найден";
                if (!picked.EndsWith("TestGame.exe", StringComparison.OrdinalIgnoreCase))
                    return "выбран не тот файл: " + Path.GetFileName(picked);
                return null;
            });

            Check("Журнал событий: запись и чтение", () =>
            {
                HistoryLog.Add("unlock_fail", detail: "попытка 1");
                HistoryLog.Add("game_start", title: "Test Game");

                var entries = HistoryLog.ReadLast(10);
                if (entries.Count < 2) return "записи не читаются";

                if (!entries.Any(e => e.Type == "game_start" && e.Title == "Test Game"))
                    return "событие game_start потерялось";
                if (!entries.Any(e => e.Type == "unlock_fail" && e.Detail == "попытка 1"))
                    return "событие unlock_fail потерялось";

                var described = HistoryLog.Describe(entries[0]);
                if (described.Length < 5) return "описание события пустое";
                return null;
            });

            Check("Достижения: каталог, прогресс и файл", () =>
            {
                var ids = Achievements.Catalog.Select(a => a.Id).ToList();
                if (ids.Count != 20) return "в каталоге не 20 достижений: " + ids.Count;
                if (ids.Distinct().Count() != ids.Count) return "повторяющиеся id в каталоге";
                if (Achievements.Catalog.Any(a => a.Target < 1)) return "цель меньше единицы";

                Achievements.Load();
                if (Achievements.UnlockedCount != 0) return "файл достижений достался не пустым";

                // Час игры — первое достижение, которое должно открыться.
                PlayStats.Add("Achievement Test", TimeSpan.FromMinutes(61));

                var fresh = Achievements.Evaluate();
                if (!fresh.Any(a => a.Id == "hour_1")) return "«Первый час» не открылся";

                if (Achievements.Evaluate().Count != 0) return "повторная проверка выдала награды заново";

                Achievements.Load();   // перечитываем файл с диска
                if (Achievements.UnlockedAt("hour_1") == null) return "достижение не попало в файл";

                var snapshot = Achievements.BuildSnapshot();
                if (snapshot.TotalSeconds < 3600) return "снимок не увидел сыгранное время";
                if (snapshot.TotalLaunches < 1) return "снимок не увидел запусков";

                return null;
            });

            Check("Отсев DLC и саундтреков в поиске", () =>
            {
                if (!ImageSearchService.IsLikelyExtraName("DOOM Eternal: The Complete Cosmetic Pack"))
                    return "набор дополнений не распознан";
                if (!ImageSearchService.IsLikelyExtraName("Hades Soundtrack"))
                    return "саундтрек не распознан";
                if (ImageSearchService.IsLikelyExtraName("DOOM Eternal"))
                    return "обычная игра отброшена";
                if (ImageSearchService.IsLikelyExtraName("The Forest"))
                    return "обычная игра отброшена (2)";
                return null;
            });

            // Сетевую проверку включаем только по запросу: TL_NET_TEST=1
            if (Environment.GetEnvironmentVariable("TL_NET_TEST") == "1")
            {
                Check("Сеть: поиск игры и скачивание обложки", () =>
                {
                    // Обязательно на пуле потоков: GetResult() на UI-потоке съел бы
                    // SynchronizationContext и приложение зависло бы в дедлоке.
                    return Task.Run(() =>
                    {
                        try
                        {
                            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                            var results = ImageSearchService.SearchAsync("Hades", cts.Token)
                                .GetAwaiter().GetResult();
                            if (results.Count == 0) return "поиск ничего не вернул";

                            var first = results[0];
                            if (!first.Name.Contains("Hades", StringComparison.OrdinalIgnoreCase))
                                return "первый результат не та игра: " + first.Name;
                            if (ImageSearchService.IsLikelyExtraName(first.Name))
                                return "в выдачу попал DLC: " + first.Name;

                            var art = ImageSearchService.FetchArtAsync("Hades", null, cts.Token)
                                .GetAwaiter().GetResult();
                            if (art?.CoverPath == null) return "обложка не скачалась";
                            if (!File.Exists(art.CoverPath)) return "файл обложки не создан";
                            if (new FileInfo(art.CoverPath).Length < 5000) return "обложка подозрительно маленькая";

                            ImageSearchService.DeleteIfMedia(art.CoverPath);
                            ImageSearchService.DeleteIfMedia(art.BackgroundPath);

                            var meta = SteamMetadataService.FetchAsync(null, "The Forest", cts.Token)
                                .GetAwaiter().GetResult();
                            if (meta == null) return "метаданные не загрузились";
                            if (meta.Description.Length < 30) return "описание пустое: '" + meta.Description + "'";
                            if (meta.Genres.Length == 0) return "жанры пустые";

                            return null;
                        }
                        catch (Exception ex)
                        {
                            return "исключение: " + ex.Message;
                        }
                    }).GetAwaiter().GetResult();
                });
            }
        }
        catch (Exception ex)
        {
            lines.Add("FAIL самопроверка упала: " + ex);
            failed = true;
        }
        finally
        {
            ConfigStore.UseDirectory(previousDirectory);
            ConfigStore.ClearLoadState();
            try { Directory.Delete(dir, recursive: true); } catch { /* файлы могли остаться занятыми */ }
        }

        lines.Insert(0, (failed ? "ПРОВАЛ" : "OK") + " — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return new SelfTestReport(!failed, string.Join(Environment.NewLine, lines));
    }
}
