using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using VstManager.App.Services;
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

public class IgnoreVersionCheckMenuTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Stop Ignoring Version Updates" : "Ignore Version Updates";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>OUTDATED badge visibility: true only when the plugin is actually outdated AND the user hasn't asked to ignore its version check. IsOutdated itself is left untouched everywhere else (sort, filter, counts).</summary>
public class OutdatedBadgeVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOutdated = values.Length > 0 && values[0] is true;
        var ignoreVersionCheck = values.Length > 1 && values[1] is true;
        return isOutdated && !ignoreVersionCheck ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Shows the "update ignored" indicator only when it is actually suppressing something — i.e. the
/// plugin really is outdated and the user has silenced the badge. IgnoreVersionCheck alone (on a
/// plugin that happens to be current) means nothing is being hidden, so no indicator is shown.
/// </summary>
/// <summary>
/// Pairs the right-clicked plugin with a "Move to Folder" entry, so a submenu item can carry both
/// in the single CommandParameter a MenuItem allows. Mirrors how the tag submenu pairs a plugin
/// with a tag. A null entry (or an "Unfiled" entry's null FolderId) means "unfile".
/// </summary>
public class FolderMoveRequestConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var plugin = values.ElementAtOrDefault(0) as PluginDisplayViewModel;
        var entry = values.ElementAtOrDefault(1) as FolderMenuEntry;
        return new FolderMoveRequest(plugin, entry?.FolderId);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible only when every bound value is true — e.g. "expanded AND non-empty".</summary>
public class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.All(v => v is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>A count as a bool ("has any"), for combining with other conditions in a MultiBinding.</summary>
public class CountToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Picks the right font for a folder's icon glyph — Segoe Fluent Icons for a vector preset,
/// the emoji font otherwise. See FolderIconGlyphs for why a single icon string can need either.
/// </summary>
public class FolderIconFontConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.Resources[
            FolderIconGlyphs.IsPreset(value as string) ? "IconGlyphFontFamily" : "EmojiFontFamily"];

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class IgnoredUpdateBadgeVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isOutdated = values.Length > 0 && values[0] is true;
        var ignoreVersionCheck = values.Length > 1 && values[1] is true;
        return isOutdated && ignoreVersionCheck ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
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
