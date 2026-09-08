using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using VstManager.Core.Services.Cloud;

namespace VstManager.Ui.Converters;

/// <summary>
/// The cloud indicator's colour: grey off, green synced, blue working, red failed — the same
/// vocabulary the WPF toolbar uses, so the two heads read identically.
/// </summary>
public class CloudStateBrushConverter : IValueConverter
{
    private static readonly IBrush Off = new SolidColorBrush(Color.Parse("#FF8B949E"));
    private static readonly IBrush Synced = new SolidColorBrush(Color.Parse("#FF10B981"));
    private static readonly IBrush Working = new SolidColorBrush(Color.Parse("#FF3B82F6"));
    private static readonly IBrush Failed = new SolidColorBrush(Color.Parse("#FFEF4444"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            CloudSyncState.Synced => Synced,
            CloudSyncState.Syncing => Working,
            CloudSyncState.Error => Failed,
            _ => Off
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
