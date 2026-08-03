using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace VstManager.App.Controls;

internal static class WindowIcon
{
    private static readonly Lazy<BitmapImage?> Default = new(LoadDefault);

    public static void ApplyDefault(Window window)
    {
        if (window.Icon == null && Default.Value is { } icon)
        {
            window.Icon = icon;
        }
    }

    private static BitmapImage? LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "a_clean_modern_app_icon_logo_design_on_a_dark_b.ico");
        if (!File.Exists(path))
        {
            return null;
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
