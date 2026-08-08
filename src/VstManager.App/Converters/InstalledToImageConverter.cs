using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VstManager.App.Converters;

public class LogoPathToBitmapConverter : IMultiValueConverter
{
    /// <summary>
    /// Logos are drawn at 72×72 on cards and 40×40 in rows, so decoding at source resolution
    /// wasted most of the pixels — and the memory holding them — for every plugin in the
    /// library. 160 stays sharp on high-DPI displays with room for the card to grow.
    /// </summary>
    private const int DecodeWidth = 160;

    /// <summary>
    /// Decoded, frozen bitmaps by path and installed-state. Bindings re-evaluate often (every
    /// filter, sort and layout change re-runs the converter for every visible plugin), and
    /// without this each pass re-decoded every image from disk.
    /// </summary>
    private static readonly ConcurrentDictionary<(string Path, bool IsInstalled), ImageSource?> Cache = new();

    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string path || string.IsNullOrEmpty(path))
        {
            return null;
        }

        var isInstalled = values[1] is true;

        // Frozen bitmaps are immutable and thread-safe, so one instance can back every binding
        // that shows the same logo.
        return Cache.GetOrAdd((path, isInstalled), static key => Decode(key.Path, key.IsInstalled));
    }

    private static ImageSource? Decode(string path, bool isInstalled)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        BitmapImage source;
        try
        {
            source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.DecodePixelWidth = DecodeWidth;
            source.UriSource = new Uri(path, UriKind.Absolute);
            source.EndInit();
            source.Freeze();
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or UriFormatException)
        {
            // The cached file's format (e.g. WebP) isn't decodable by WPF's imaging pipeline.
            return null;
        }

        if (isInstalled)
        {
            return source;
        }

        try
        {
            var grayscale = new FormatConvertedBitmap();
            grayscale.BeginInit();
            grayscale.Source = source;
            grayscale.DestinationFormat = PixelFormats.Gray8;
            grayscale.EndInit();
            grayscale.Freeze();
            return grayscale;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Drops a path from the cache so a re-downloaded logo is picked up. Without this, fixing a
    /// plugin's artwork would keep showing the old image until restart, since the file path
    /// doesn't change.
    /// </summary>
    public static void Invalidate(string path)
    {
        Cache.TryRemove((path, true), out _);
        Cache.TryRemove((path, false), out _);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InstalledToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.5;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
