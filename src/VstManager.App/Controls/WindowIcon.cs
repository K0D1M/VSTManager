using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using VstManager.App.Services;

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

    /// <summary>
    /// The window icon is decorative, so any decode failure must degrade to "no custom icon"
    /// rather than take the window's constructor — and with it the whole app — down. That is not
    /// hypothetical: WPF's ICO decoder is pickier than the shell's and throws outright on files
    /// whose frames are PNG-compressed, which is how the original app icon was authored. The
    /// icon shipped now (see <see cref="AppIdentityService.IconPath"/>) decodes cleanly, but the
    /// guard stays so replacing the artwork can never again turn into a startup crash.
    /// </summary>
    private static BitmapImage? LoadDefault()
    {
        var path = AppIdentityService.IconPath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is FileFormatException or NotSupportedException or COMException or IOException)
        {
            return null;
        }
    }
}
