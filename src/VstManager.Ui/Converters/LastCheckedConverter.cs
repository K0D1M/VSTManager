using System.Globalization;
using Avalonia.Data.Converters;

namespace VstManager.Ui.Converters;

/// <summary>Ported from VstManager.App/Converters/TagConverters.cs — same wording exactly.</summary>
public class LastCheckedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime lastChecked
            ? $"Last checked: {lastChecked:g}"
            : "Last checked: never";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
