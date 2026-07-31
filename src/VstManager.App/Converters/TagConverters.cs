using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VstManager.App.ViewModels;
using VstManager.Core.Models;

namespace VstManager.App.Converters;

public class ScanningLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Scanning..." : "Rescan";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class AnyTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Any(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class FavoriteStarGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "★" : "☆";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !(value is true);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        !(value is true);
}

public class TagToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            PluginTag.Legit or PluginTagSummary.Legit => "LegitBrush",
            PluginTag.Cracked or PluginTagSummary.Cracked => "CrackedBrush",
            PluginTagSummary.Both => "TealAccentBrush",
            _ => "UnclassifiedBrush"
        };

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class TagToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            PluginTag.Legit or PluginTagSummary.Legit => "Legit",
            PluginTag.Cracked or PluginTagSummary.Cracked => "Cracked",
            PluginTagSummary.Both => "Legit + Cracked",
            _ => "Unclassified"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToThemeLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Dark" : "Light";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class HideMenuTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Unhide" : "Hide";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class HiddenToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 0.5 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InstalledDotBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Brushes.LimeGreen : Brushes.Crimson;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InstalledDotTooltipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Installed" : "Not installed";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.Equals(parameter) ?? false;

    public object? ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter : Binding.DoNothing;
}

public class CountToVisibilityConverter : IValueConverter
{
    /// <summary>Pass "Inverse" to show only when the count is zero (e.g. empty-state hints).</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasItems = value is int count && count > 0;
        var inverse = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);

        return hasItems != inverse ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class EnumEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value?.Equals(parameter) ?? false) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class ColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color color ? new SolidColorBrush(color) : Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class ModeToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ManagementMode.Legit;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? ManagementMode.Legit : ManagementMode.Cracked;
}

public class BoolToOnOffConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "On" : "Off";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class LastCheckedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime lastChecked
            ? $"Last checked: {lastChecked:g}"
            : "Last checked: never";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isEmpty = string.IsNullOrEmpty(value as string);
        var inverse = string.Equals(parameter as string, "Inverse", StringComparison.OrdinalIgnoreCase);

        var isVisible = inverse ? isEmpty : !isEmpty;
        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// PluginFormat is inferred purely from the file extension at scan time (.vst3 → Vst3,
/// .dll → Vst2), so this label doubles as an explanation of that distinction wherever a
/// per-copy row is shown (e.g. the detail window's install path list).
/// </summary>
public class FormatToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            PluginFormat.Vst3 => "VST3",
            PluginFormat.Vst2 => "VST2",
            _ => string.Empty
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InstalledFilterToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not InstalledFilterOption filter || parameter is not string section)
        {
            return Visibility.Visible;
        }

        var isHidden = section switch
        {
            "Installed" => filter == InstalledFilterOption.NotInstalledOnly,
            "NotInstalled" => filter == InstalledFilterOption.InstalledOnly,
            _ => false
        };

        return isHidden ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
