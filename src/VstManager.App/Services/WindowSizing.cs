using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VstManager.App.Services;

/// <summary>
/// Keeps windows inside the monitor they actually open on. WPF's own sizing has no notion of
/// the work area, so a window whose designed size exceeds a smaller screen (or one restored
/// onto a secondary monitor) can open partly offscreen — and if its Min size is larger than the
/// screen, the user can't drag it back to a usable size either. Everything here is best-effort:
/// if the Win32 monitor query fails it falls back to the primary monitor's work area.
/// </summary>
public static class WindowSizing
{
    /// <summary>Leave a little room so a maximum-size window doesn't sit flush against the screen edges.</summary>
    private const double Margin = 16;

    /// <summary>
    /// Shrinks the window to fit the current monitor's work area (respecting the taskbar) and
    /// nudges it back on-screen if it would hang off an edge. Also lowers MinWidth/MinHeight
    /// when they alone exceed the screen, so the window stays resizable on small displays.
    /// </summary>
    public static void FitToScreen(Window window)
    {
        if (window.WindowState != WindowState.Normal)
        {
            return;
        }

        var work = GetWorkArea(window);
        var maxWidth = work.Width - Margin;
        var maxHeight = work.Height - Margin;

        // A Min larger than the screen would make the window impossible to shrink.
        if (window.MinWidth > maxWidth)
        {
            window.MinWidth = maxWidth;
        }

        if (window.MinHeight > maxHeight)
        {
            window.MinHeight = maxHeight;
        }

        if (!double.IsNaN(window.Width) && window.Width > maxWidth)
        {
            window.Width = maxWidth;
        }

        if (!double.IsNaN(window.Height) && window.Height > maxHeight)
        {
            window.Height = maxHeight;
        }

        // Pull back on-screen if the window (often one centred on an owner spanning monitors)
        // would otherwise straddle an edge.
        if (window.Left < work.Left)
        {
            window.Left = work.Left + Margin / 2;
        }

        if (window.Top < work.Top)
        {
            window.Top = work.Top + Margin / 2;
        }

        if (window.Left + window.ActualWidth > work.Right)
        {
            window.Left = Math.Max(work.Left, work.Right - window.ActualWidth - Margin / 2);
        }

        if (window.Top + window.ActualHeight > work.Bottom)
        {
            window.Top = Math.Max(work.Top, work.Bottom - window.ActualHeight - Margin / 2);
        }
    }

    /// <summary>Work area (screen minus taskbar) of the monitor this window is on, in DIPs.</summary>
    public static Rect GetWorkArea(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };

                if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
                {
                    // Win32 reports physical pixels; convert to the DIPs WPF sizes windows in.
                    var source = PresentationSource.FromVisual(window);
                    var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                    var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

                    if (scaleX > 0 && scaleY > 0)
                    {
                        return new Rect(
                            info.rcWork.left / scaleX,
                            info.rcWork.top / scaleY,
                            (info.rcWork.right - info.rcWork.left) / scaleX,
                            (info.rcWork.bottom - info.rcWork.top) / scaleY);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Fall through to the primary-monitor approximation below.
        }

        return SystemParameters.WorkArea;
    }

    private const int MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect32
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int cbSize;
        public Rect32 rcMonitor;
        public Rect32 rcWork;
        public int dwFlags;
    }
}
