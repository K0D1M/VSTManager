using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VstManager.Ui.Converters;

/// <summary>
/// Picks IconEye (open) or IconEyeOff (crossed) from Icons.axaml based on ShowHidden, so the
/// toolbar's Hidden toggle and the Filter popup's "Show hidden plugins" switch both reflect their
/// actual current state rather than a single static icon.
/// </summary>
public class BoolToEyeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is true ? "IconEye" : "IconEyeOff";
        object? resource = null;
        var found = Application.Current is { } app && app.TryGetResource(key, null, out resource);
        return found ? resource as StreamGeometry : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
