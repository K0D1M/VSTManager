using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace VstManager.Ui.Themes;

/// <summary>How the app picks its light/dark appearance.</summary>
public enum AppThemeMode
{
    /// <summary>Follow the operating system, and keep following it as it changes.</summary>
    System,
    Light,
    Dark
}

/// <summary>
/// Owns the theme variant and the user-configurable accent colour.
///
/// Avalonia resolves palette tokens from the active <see cref="ThemeVariant"/> on its own, so
/// switching appearance is a single assignment rather than the dictionary swap the WPF app did.
/// The accent is the part that still needs code: it is a user choice layered over whichever
/// variant is active, so it has to be re-applied whenever the variant changes or the custom
/// colour would revert to the palette's seeded default.
/// </summary>
public static class ThemeService
{
    private static Color _accent = Color.Parse("#FF8A5CF6");

    public static AppThemeMode Mode { get; private set; } = AppThemeMode.System;

    public static Color Accent => _accent;

    /// <summary>True when the app is currently painting dark, whatever the mode that produced it.</summary>
    public static bool IsDark =>
        Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public static void Apply(AppThemeMode mode)
    {
        Mode = mode;

        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = mode switch
        {
            AppThemeMode.Light => ThemeVariant.Light,
            AppThemeMode.Dark => ThemeVariant.Dark,
            // Default hands the choice back to the OS and tracks it live.
            _ => ThemeVariant.Default
        };

        // The variant swap re-seeds the palette's accent tokens, so put the user's colour back.
        ApplyAccent(_accent);
    }

    /// <summary>
    /// Sets the accent and derives its hover shade, matching the WPF app's behaviour of
    /// lightening by 12% toward white.
    /// </summary>
    public static void ApplyAccent(Color color)
    {
        _accent = color;

        if (Application.Current is not { } app)
        {
            return;
        }

        app.Resources["AccentColor"] = color;
        app.Resources["AccentColorHover"] = Lighten(color, 0.12);
    }

    private static Color Lighten(Color color, double amount)
    {
        byte Adjust(byte channel) => (byte)Math.Min(255, channel + (255 - channel) * amount);
        return Color.FromArgb(color.A, Adjust(color.R), Adjust(color.G), Adjust(color.B));
    }
}
