using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TopRacerLauncher;

/// <summary>
/// Кэш декодированных картинок. Раньше каждое перестроение библиотеки заново читало
/// и декодировало все обложки и фоны на UI-потоке — на большой библиотеке это фриз.
/// </summary>
public static class ImageCache
{
    private const int MaxEntries = 400;

    private static readonly Dictionary<string, ImageSource?> Items = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> Order = new();

    /// <summary>Возвращает картинку с уменьшением до decodeWidth (null — файла нет/не читается).</summary>
    public static ImageSource? Load(string? path, int decodeWidth)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string key;
        try
        {
            if (path.Contains("://", StringComparison.Ordinal)) return null;

            var info = new FileInfo(path);
            if (!info.Exists) return null;

            // Ключ включает время изменения файла: подменили картинку — кэш сам обновится.
            key = info.FullName + "|" + info.LastWriteTimeUtc.Ticks + "|" + decodeWidth;
        }
        catch
        {
            return null;
        }

        lock (Items)
        {
            if (Items.TryGetValue(key, out var cached)) return cached;
        }

        var image = Decode(path, decodeWidth);

        lock (Items)
        {
            Items[key] = image;
            Order.Enqueue(key);
            while (Order.Count > MaxEntries)
                Items.Remove(Order.Dequeue());
        }

        return image;
    }

    private static ImageSource? Decode(string path, int decodeWidth)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.DecodePixelWidth = Math.Max(1, decodeWidth);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Сбрасывает кэш (например, после массовой замены обложек).</summary>
    public static void Clear()
    {
        lock (Items)
        {
            Items.Clear();
            Order.Clear();
        }
    }
}
