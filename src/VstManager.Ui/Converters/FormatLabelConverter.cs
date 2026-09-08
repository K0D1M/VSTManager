using System.Globalization;
using Avalonia.Data.Converters;
using VstManager.Core.Models;

namespace VstManager.Ui.Converters;

/// <summary>PluginFormat as the label users know it by, rather than the enum's "Vst2".</summary>
public class FormatLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is PluginFormat f ? (f == PluginFormat.Vst2 ? "VST2" : "VST3") : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
