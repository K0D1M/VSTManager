using System.Globalization;
using Avalonia.Data.Converters;
using VstManager.Core.Services.Cloud;

namespace VstManager.Ui.Converters;

/// <summary>
/// True only while actively syncing, to drive the cloud icon's pulse animation — the same
/// state/effect split the WPF toolbar uses (its CloudStateIsSyncingConverter).
/// </summary>
public class CloudStateIsSyncingConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CloudSyncState.Syncing;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
