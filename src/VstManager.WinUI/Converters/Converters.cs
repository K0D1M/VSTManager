using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VstManager.Core.Models;
using Windows.UI;

namespace VstManager.WinUI.Converters;

/// <summary>
/// Note the signature difference from WPF: WinUI's IValueConverter takes a `string language`
/// instead of a CultureInfo, and WinUI has no IMultiValueConverter at all — so the WPF head's
/// multi-binding logo converter is split into this plus an opacity binding.
/// </summary>
public class PathToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            // Decode to roughly the card's display size rather than full resolution — the same
            // reasoning as the WPF head, where full-res decoding of ~200 logos was pure waste.
            return new BitmapImage(new Uri(path)) { DecodePixelWidth = 160 };
        }
        catch (Exception ex) when (ex is UriFormatException or IOException)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Uninstalled plugins are dimmed rather than greyscaled — cheaper and reads the same.</summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? 1.0 : 0.45;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is true;
        if (parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Tag colours are stored as #AARRGGBB strings; WinUI needs a Brush.</summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
        {
            return new SolidColorBrush(Colors.Gray);
        }

        try
        {
            return new SolidColorBrush(ParseColor(hex));
        }
        catch (FormatException)
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    internal static Color ParseColor(string hex)
    {
        var text = hex.TrimStart('#');
        if (text.Length == 6)
        {
            text = "FF" + text;
        }

        if (text.Length != 8 || !uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var packed))
        {
            throw new FormatException($"'{hex}' is not #AARRGGBB or #RRGGBB.");
        }

        return Color.FromArgb(
            (byte)((packed >> 24) & 0xFF),
            (byte)((packed >> 16) & 0xFF),
            (byte)((packed >> 8) & 0xFF),
            (byte)(packed & 0xFF));
    }
}

/// <summary>Legit / Cracked / Both / Unclassified → the badge colour.</summary>
public class TagSummaryToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new SolidColorBrush(value is PluginTagSummary summary
            ? summary switch
            {
                PluginTagSummary.Legit => Color.FromArgb(0xFF, 0x34, 0xD3, 0x99),
                PluginTagSummary.Cracked => Color.FromArgb(0xFF, 0xF8, 0x71, 0x71),
                PluginTagSummary.Both => Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B),
                _ => Color.FromArgb(0xFF, 0x64, 0x74, 0x8B)
            }
            : Colors.Gray);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

/// <summary>Filled vs outline star, from the Segoe Fluent icon set.</summary>
public class FavoriteGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? "" : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public class TagSummaryToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is PluginTagSummary summary
            ? summary switch
            {
                PluginTagSummary.Legit => "Legit",
                PluginTagSummary.Cracked => "Cracked",
                PluginTagSummary.Both => "Mixed",
                _ => "Unclassified"
            }
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
