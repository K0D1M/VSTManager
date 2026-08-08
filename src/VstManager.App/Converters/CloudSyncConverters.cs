using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using VstManager.App.ViewModels;
using VstManager.Core.Models;
using VstManager.Core.Services.Cloud;

namespace VstManager.App.Converters;

/// <summary>
/// Colours the toolbar cloud glyph by sync state: green synced, blue syncing, red failed, grey
/// not set up. Grey matters — a red cloud on a fresh install would read as "broken" when nothing
/// has been configured yet.
/// </summary>
public class CloudStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Synced = Frozen("#FF3FB950");
    private static readonly SolidColorBrush Syncing = Frozen("#FF3B9EFF");
    private static readonly SolidColorBrush Error = Frozen("#FFE5534B");
    private static readonly SolidColorBrush Idle = Frozen("#FF8B949E");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CloudSyncState.Synced => Synced,
        CloudSyncState.Syncing => Syncing,
        CloudSyncState.Error => Error,
        _ => Idle
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Turns a tag's stored colour string into a brush. Tag colours are user-editable and stored as
/// text, so an unparseable value has to degrade to a neutral grey rather than throw mid-render.
/// Brushes are cached and frozen — the same handful of colours is asked for once per visible
/// chip, on every list refresh.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SolidColorBrush> Cache = new();
    private static readonly SolidColorBrush Fallback = CreateFrozen("#FF8B949E");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
        {
            return Fallback;
        }

        return Cache.GetOrAdd(hex, static key =>
        {
            try
            {
                return CreateFrozen(key);
            }
            catch (FormatException)
            {
                return Fallback;
            }
        });
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush CreateFrozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>True only while syncing — drives the pulse animation on the cloud glyph.</summary>
public class CloudStateIsSyncingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CloudSyncState.Syncing;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Short label for the sort button, so the active sort reads at a glance.</summary>
public class SortOptionLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        SortOption.Vendor => "Vendor",
        SortOption.Type => "Type",
        SortOption.RecentlyAdded => "Newest",
        SortOption.UpdateStatus => "Updates",
        _ => "Name"
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Packs a plugin and a tag into one command parameter. Toggling a tag needs both, and a
/// MenuItem's CommandParameter is a single value — inside the tag submenu the DataContext is the
/// tag, so the plugin has to be reached separately and combined here.
/// </summary>
public class TagCommandArgsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        new TagCommandArgs(
            values.ElementAtOrDefault(0) as PluginDisplayViewModel,
            values.ElementAtOrDefault(1) as TagDefinition);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
