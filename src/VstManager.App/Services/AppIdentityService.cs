using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VstManager.App.Services;

/// <summary>
/// Gives the process a stable AppUserModelID and registers it as a notification sender.
///
/// Without this, a <see cref="NotificationService"/> balloon never becomes a real toast: on
/// Windows 10 1709+ the shell only promotes a NotifyIcon balloon into Action Center when the
/// calling process has an AppUserModelID *and* a matching registration under the notification
/// settings key. Unpackaged WPF apps have neither by default, so balloons silently degraded to
/// legacy tray tooltips — a brief popup by the clock that leaves no Action Center entry.
/// Confirmed empirically: before this, firing a notification left wpndatabase.db untouched and
/// the app absent from the registered-sender list.
///
/// The AppUserModelID must be set before any window is created (the shell caches the value the
/// first time it needs it), which is why <see cref="Register"/> runs at the very top of startup.
/// </summary>
public static class AppIdentityService
{
    /// <summary>
    /// Identifies this app to the shell for toasts, taskbar grouping and jump lists. Must stay
    /// stable across releases: changing it makes Windows treat the app as a brand new sender and
    /// drops any notification preference the user had set for it.
    /// </summary>
    public const string AppUserModelId = "K0D1M.VstManager";

    /// <summary>Name shown as the notification's sender in Action Center and in Windows' notification settings.</summary>
    private const string DisplayName = "VST Manager";

    /// <summary>
    /// The app icon as a file on disk, for the places that load it at runtime rather than from
    /// the embedded resource: the tray icon, notification balloons, the Action Center sender
    /// entry, and the WPF window icon.
    ///
    /// Deliberately not the original "a_clean_modern_app_icon..." file: every frame in that one
    /// is PNG-compressed, which WPF's ICO decoder rejects outright (it throws rather than falling
    /// back to a frame it can read), and which the shell won't render for the notification sender
    /// icon either. This one carries the same artwork as uncompressed 32bpp frames at 16..256px,
    /// which both WPF and the shell accept.
    /// </summary>
    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "app-icon.ico");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    /// <summary>
    /// Creates the Start Menu shortcut the toast UI reads its sender name and icon from.
    ///
    /// The registry sender entry alone is not enough: with only that, toasts render with the raw
    /// AppUserModelID as the header ("K0D1M.VstManager") and no icon at all. Windows resolves the
    /// friendly name and logo by finding a Start Menu shortcut whose System.AppUserModel.ID
    /// property matches the calling process's, so the shortcut has to exist and carry that
    /// property. Verified by capturing the live toast before and after.
    ///
    /// Rewritten only when missing or stale, so this costs nothing on a normal launch and follows
    /// the exe if the app is moved.
    /// </summary>
    private static void EnsureStartMenuShortcut()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        var shortcutPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs", $"{DisplayName}.lnk");

        if (File.Exists(shortcutPath) && File.GetLastWriteTimeUtc(shortcutPath) >= File.GetLastWriteTimeUtc(exePath))
        {
            return;
        }

        // IShellLink + IPropertyStore via the ShellLink COM class. Done with raw COM interop
        // rather than the WScript.Shell script object because only the property-store route can
        // stamp System.AppUserModel.ID onto the shortcut, which is the whole point of writing it.
        var link = (IShellLinkW)new CShellLink();
        link.SetPath(exePath);
        link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? string.Empty);
        link.SetIconLocation(File.Exists(IconPath) ? IconPath : exePath, 0);

        var store = (IPropertyStore)link;
        var appIdKey = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        var value = new PropVariant(AppUserModelId);
        try
        {
            store.SetValue(ref appIdKey, ref value);
            store.Commit();

            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
            ((IPersistFile)link).Save(shortcutPath, true);
        }
        finally
        {
            value.Clear();
            Marshal.ReleaseComObject(store);
            Marshal.ReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int maxArgs);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        private Guid _formatId = formatId;
        private uint _propertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort _valueType;
        [FieldOffset(8)] private IntPtr _pointerValue;

        private const ushort VtLpwstr = 31;

        public PropVariant(string value)
        {
            _valueType = VtLpwstr;
            _pointerValue = Marshal.StringToCoTaskMemUni(value);
        }

        public void Clear()
        {
            if (_pointerValue != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(_pointerValue);
                _pointerValue = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Best-effort: notifications are a convenience, so a machine that refuses the registry write
    /// (locked-down policy, roaming profile quirk) must still get a working app rather than a
    /// startup crash. A failure here costs Action Center integration and nothing else.
    /// </summary>
    public static void Register()
    {
        var iconPath = IconPath;

        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
        }
        catch (Exception ex) when (ex is COMException or EntryPointNotFoundException or DllNotFoundException)
        {
            return;
        }

        try
        {
            EnsureStartMenuShortcut();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or InvalidCastException)
        {
            // Without the shortcut, toasts still appear but fall back to showing the raw
            // AppUserModelID and no icon — degraded, not broken.
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings\{AppUserModelId}");

            if (key is null)
            {
                return;
            }

            // Only seed DisplayName/IconUri — deliberately not "Enabled". Windows writes that
            // itself, and the user owns it from the Settings app; forcing it on every launch
            // would silently re-enable notifications they had turned off for this app.
            key.SetValue("DisplayName", DisplayName, RegistryValueKind.String);

            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                key.SetValue("IconUri", iconPath, RegistryValueKind.String);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // Leave the AppUserModelID set — it alone is enough for the shell to route toasts.
        }
    }
}
