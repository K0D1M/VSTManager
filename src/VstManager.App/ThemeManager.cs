using System.Windows;
using System.Windows.Media;

namespace VstManager.App;

public enum AppTheme
{
    Dark,
    Light
}

public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = AppTheme.Dark;
    public static Color AccentColor { get; private set; } = (Color)ColorConverter.ConvertFromString("#FF8A5CF6");

    public static void Apply(AppTheme theme)
    {
        Current = theme;
        var uri = theme == AppTheme.Dark
            ? new Uri("Themes/Dark.xaml", UriKind.Relative)
            : new Uri("Themes/Light.xaml", UriKind.Relative);

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var themeDictionary = new ResourceDictionary { Source = uri };
        dictionaries[0] = themeDictionary;

        ApplyAccent(AccentColor);
    }

    public static void ApplyAccent(Color color)
    {
        AccentColor = color;
        var resources = Application.Current.Resources;

        resources["AccentColor"] = color;
        resources["AccentBrush"] = new SolidColorBrush(color);

        var hoverColor = Lighten(color, 0.12);
        resources["AccentColorHover"] = hoverColor;
        resources["AccentHoverBrush"] = new SolidColorBrush(hoverColor);
    }

    private static Color Lighten(Color color, double amount)
    {
        byte Adjust(byte channel) => (byte)Math.Min(255, channel + (255 - channel) * amount);
        return Color.FromArgb(color.A, Adjust(color.R), Adjust(color.G), Adjust(color.B));
    }
}
