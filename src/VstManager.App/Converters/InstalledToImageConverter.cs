using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VstManager.App.Converters;

public class LogoPathToBitmapConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        var isInstalled = values[1] is true;

        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(path, UriKind.Absolute);
        source.EndInit();
        source.Freeze();

        if (isInstalled)
        {
            return source;
        }

        var grayscale = new FormatConvertedBitmap();
        grayscale.BeginInit();
        grayscale.Source = source;
        grayscale.DestinationFormat = PixelFormats.Gray8;
        grayscale.EndInit();
        grayscale.Freeze();
        return grayscale;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class InstalledToOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 1.0 : 0.5;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
