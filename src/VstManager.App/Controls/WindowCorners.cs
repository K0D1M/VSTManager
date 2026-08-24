using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VstManager.App.Controls;

/// <summary>
/// Rounds a window's desktop-composited corners on Windows 11 via the DWM corner-preference API.
///
/// The WindowChrome.CornerRadius clips the *client* area, but the actual outer corner of a
/// borderless window is drawn by the desktop compositor — only this call rounds that. On Windows 10
/// and earlier the attribute is unknown and the call is ignored (corners stay square), so it's
/// wrapped to never throw.
///
/// Corners are squared while maximized, matching how Windows expects maximized/snapped windows to
/// look, and restored to round when the window returns to Normal.
/// </summary>
public static class WindowCorners
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    private enum CornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Enables rounded corners for the window and keeps them in step with its state.</summary>
    public static void Apply(Window window)
    {
        if (window.IsInitialized)
        {
            Hook(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => Hook(window);
        }
    }

    private static void Hook(Window window)
    {
        UpdateForState(window);
        window.StateChanged += (_, _) => UpdateForState(window);
    }

    private static void UpdateForState(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var preference = (int)(window.WindowState == WindowState.Maximized
            ? CornerPreference.DoNotRound
            : CornerPreference.Round);

        try
        {
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // No dwmapi (very old Windows) — corners stay square, which is fine.
        }
        catch (EntryPointNotFoundException)
        {
            // Present but older — same graceful fallback.
        }
    }
}
