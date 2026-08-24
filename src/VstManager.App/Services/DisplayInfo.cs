using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace VstManager.App.Services;

/// <summary>
/// Reports the refresh rate of the monitor a window sits on, so animations can be sampled at the
/// display's cadence rather than WPF's fixed ~60 fps default — which on a 120/144/240 Hz monitor
/// otherwise looks like 60 fps with repeated frames.
/// </summary>
public static class DisplayInfo
{
    private const int MonitorDefaultToNearest = 2;
    private const int EnumCurrentSettings = -1;

    /// <summary>Refresh rate per monitor handle. The value is stable for a given monitor/mode.</summary>
    private static readonly ConcurrentDictionary<IntPtr, int> Cache = new();

    /// <summary>
    /// The refresh rate (Hz) of the monitor showing <paramref name="visual"/>, or 60 when it
    /// can't be determined. Clamped to a sane range so a driver reporting 0/1 ("use default")
    /// or something absurd never drives an animation frame rate.
    /// </summary>
    public static int GetRefreshRate(Visual? visual)
    {
        try
        {
            var hwnd = visual is not null && PresentationSource.FromVisual(visual) is HwndSource source
                ? source.Handle
                : IntPtr.Zero;

            if (hwnd == IntPtr.Zero)
            {
                return 60;
            }

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return 60;
            }

            return Cache.GetOrAdd(monitor, QueryRefreshRate);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 60;
        }
    }

    private static int QueryRefreshRate(IntPtr monitor)
    {
        var info = new MonitorInfoEx { cbSize = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return 60;
        }

        var devMode = new DevMode { dmSize = (ushort)Marshal.SizeOf<DevMode>() };
        if (!EnumDisplaySettings(info.szDevice, EnumCurrentSettings, ref devMode))
        {
            return 60;
        }

        var hz = (int)devMode.dmDisplayFrequency;
        return hz is >= 30 and <= 480 ? hz : 60;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int cbSize;
        public Rect rcMonitor;
        public Rect rcWork;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public ushort dmSpecVersion;
        public ushort dmDriverVersion;
        public ushort dmSize;
        public ushort dmDriverExtra;
        public uint dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public uint dmDisplayOrientation;
        public uint dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public ushort dmLogPixels;
        public uint dmBitsPerPel;
        public uint dmPelsWidth;
        public uint dmPelsHeight;
        public uint dmDisplayFlags;
        public uint dmDisplayFrequency;
        public uint dmICMMethod;
        public uint dmICMIntent;
        public uint dmMediaType;
        public uint dmDitherType;
        public uint dmReserved1;
        public uint dmReserved2;
        public uint dmPanningWidth;
        public uint dmPanningHeight;
    }
}
