using System.Configuration;
using System.Data;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VstManager.App.Services;

namespace VstManager.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    // Shares the installer's AppId rather than inventing a second identifier: an Inno Setup
    // AppId and a named Mutex live in unrelated Windows namespaces (a registry key under
    // Uninstall\{GUID} versus a kernel synchronization object), so there is no collision risk in
    // reusing it — and doing so makes "this mutex belongs to this installed app" self-evident.
    // Keep this 1:1 with the app: a second, unrelated mutex (e.g. for a separate updater process)
    // must get its own GUID instead of sharing this one.
    private const string SingleInstanceMutexName = "VstManager-SingleInstance-8D7A4B52-EF7E-422B-9E36-C30CFDB13802";

    private Mutex? _singleInstanceMutex;

    // Whether THIS process actually acquired ownership. OnExit runs on both the winning and the
    // losing path (Shutdown() during OnStartup still fires it), and calling ReleaseMutex() on a
    // mutex this process never owned throws ApplicationException — caught by testing a real
    // second launch, not by reading the code.
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Checked before any other startup work — a second instance should exit as early and
        // cheaply as possible, without registering handlers or touching the notification system.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;

        if (!createdNew)
        {
            ShowMessageBox(
                "VST Manager is already running.",
                "VST Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Shutdown() during OnStartup exits cleanly before any window is created, so the
            // already-running instance is left completely undisturbed.
            Shutdown();
            return;
        }

        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Before any window exists: the shell caches this process's AppUserModelID the first
        // time it needs one, and without it notification balloons never reach Action Center.
        AppIdentityService.Register();

        // Registered as a class handler so every ScrollViewer gets smooth wheel scrolling,
        // including ones generated inside control templates.
        SmoothScroll.EnableGlobally();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // This fires on both the owning and the non-owning path — only release what was
        // actually acquired.
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowMessageBox(
            $"Something went wrong and the action couldn't be completed:\n\n{Describe(e.Exception)}",
            "VST Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    // Lazily created, reused for the app's lifetime: one throwaway native window is enough to
    // own every top-level MessageBox.
    private static Window? _messageBoxIconOwner;

    /// <summary>
    /// MessageBox.Show with no owner window falls back to a generic system icon in its title bar
    /// instead of this app's icon — visible confirmation came from actually triggering the
    /// single-instance dialog and looking at it, not from reading the WPF docs. An unowned dialog
    /// mid-startup (this app's own MainWindow may not exist yet) has nothing to inherit from, so
    /// a small invisible window carrying the real icon is created purely to be that owner.
    /// </summary>
    private static void ShowMessageBox(string message, string title, MessageBoxButton button, MessageBoxImage image) =>
        MessageBox.Show(GetMessageBoxIconOwner(), message, title, button, image);

    private static Window GetMessageBoxIconOwner()
    {
        if (_messageBoxIconOwner is not null)
        {
            return _messageBoxIconOwner;
        }

        _messageBoxIconOwner = new Window
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Width = 0,
            Height = 0,
            Left = -10000,
            Top = -10000,
            Icon = new BitmapImage(new Uri(AppIdentityService.IconPath))
        };

        // Forces the native window (and with it, the icon WM_GETICON reports) to exist without
        // ever calling Show() — WPF applies Window.Icon to the real handle as soon as it is
        // created, not only once the window becomes visible.
        new WindowInteropHelper(_messageBoxIconOwner).EnsureHandle();
        return _messageBoxIconOwner;
    }

    /// <summary>
    /// Unwraps the exception chain. The outermost message is often a wrapper that names nothing
    /// useful — a failure inside a window's constructor surfaces as "the invocation of the
    /// constructor ... threw an exception", with the actual cause buried in InnerException — so
    /// reporting only the top-level message makes a startup failure impossible to diagnose.
    /// The deepest frame's stack trace is included because that's where the fault actually is.
    /// </summary>
    private static string Describe(Exception exception)
    {
        var messages = new List<string>();
        var current = exception;
        Exception deepest = exception;

        while (current is not null)
        {
            messages.Add($"{current.GetType().Name}: {current.Message}");
            deepest = current;
            current = current.InnerException;
        }

        var trace = deepest.StackTrace;
        if (!string.IsNullOrWhiteSpace(trace))
        {
            var lines = trace.Split('\n').Take(6).Select(l => l.TrimEnd());
            messages.Add(string.Empty);
            messages.Add(string.Join('\n', lines));
        }

        return string.Join("\n\n", messages);
    }
}

