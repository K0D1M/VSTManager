using System.Globalization;
using Avalonia.Data.Converters;

namespace VstManager.Ui.Converters;

/// <summary>Ported from WPF's FavoriteStarGlyphConverter (TagConverters.cs:30-37) — same glyphs.</summary>
public class BoolToFavoriteGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "★" : "☆";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
