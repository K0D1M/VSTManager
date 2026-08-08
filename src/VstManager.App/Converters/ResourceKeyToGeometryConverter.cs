using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace VstManager.App.Converters;

/// <summary>
/// Resolves a resource key string (e.g. "IconGeneral") to the Geometry it names in
/// Styles.xaml. Lets a settings tab's header specify its icon as plain text
/// (<see cref="Controls.SettingsTabHeader.Icon"/>) rather than an {x:Static}-style reference,
/// which XAML has no clean syntax for when the key is itself data-bound per tab.
/// </summary>
public class ResourceKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current.TryFindResource(key) as Geometry : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
