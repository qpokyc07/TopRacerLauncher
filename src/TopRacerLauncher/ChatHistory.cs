using System.IO;
using System.Text;
using System.Text.Json;

namespace TopRacerLauncher;

/// <summary>Сообщение в истории чата.</summary>
public sealed class ChatEntry
{
    public DateTime Ts { get; set; }
    public string From { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>История чата с друзьями: по файлу на друга в %APPDATA%\TopRacerLauncher\chat.</summary>
public static class ChatHistory
{
    private const int MaxMessages = 200;

    public static string Directory => Path.Combine(ConfigStore.ConfigDirectory, "chat");

    private static string PathFor(string peerId)
    {
        var safe = new string(peerId.Where(char.IsLetterOrDigit).ToArray());
        if (safe.Length == 0) safe = "peer";
        return Path.Combine(Directory, safe + ".jsonl");
    }

    public static List<ChatEntry> Load(string peerId)
    {
        var list = new List<ChatEntry>();

        try
        {
            var path = PathFor(peerId);
            if (!File.Exists(path)) return list;

            foreach (var line in File.ReadLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<ChatEntry>(line);
                    if (entry != null) list.Add(entry);
                }
                catch { /* битая строка */ }
            }
        }
        catch { /* нет доступа — пустая история */ }

        if (list.Count > MaxMessages)
            list = list.Skip(list.Count - MaxMessages).ToList();

        return list;
    }

    public static void Append(string peerId, string from, string text)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var entry = new ChatEntry { Ts = DateTime.UtcNow, From = from, Text = text };
            File.AppendAllText(PathFor(peerId), JsonSerializer.Serialize(entry) + Environment.NewLine, Encoding.UTF8);
        }
        catch { /* история не критична */ }
    }
}
