using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VstManager.Ui.Converters;

/// <summary>A stored "#AARRGGBB" tag colour as a brush; grey when the string is unparseable.</summary>
public class HexToBrushConverter : IValueConverter
{
    private static readonly IBrush Fallback = new SolidColorBrush(Color.Parse("#FF8B949E"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string hex && Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Fallback;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
