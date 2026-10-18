using System.Globalization;
using Avalonia.Data.Converters;

namespace VstManager.Ui.Converters;

/// <summary>
/// True when the two bound values are equal — e.g. a category chip's id against the staged
/// selection, driving its <c>selected</c> class.
/// </summary>
public class EqualityMultiConverter : IMultiValueConverter
{
    public static EqualityMultiConverter Instance { get; } = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count == 2 && values[0] is not null && Equals(values[0], values[1]);
}
