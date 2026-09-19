using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VstManager.Core.Services;
using VstManager.Ui.Views;

namespace VstManager.Ui;

public partial class App : Application
{
    // Same GUID as the WPF app's mutex (VstManager.App/App.xaml.cs) — the two heads are one app
    // to the user, so only one of either should ever run at a time, matching the autostart
    // Run-value collision this project already documents for the same reason.
    private const string SingleInstanceMutexName = "VstManager-SingleInstance-8D7A4B52-EF7E-422B-9E36-C30CFDB13802";

    private Mutex? _singleInstanceMutex;

    // OnExit-equivalent cleanup runs on both the winning and the losing path here too (see
    // OnFrameworkInitializationCompleted's early return), and releasing a mutex this process
    // never actually acquired throws — same guard WPF's App.xaml.cs uses.
    private bool _ownsSingleInstanceMutex;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Explicit, because hiding the window to the tray leaves zero open windows: under the
            // default OnLastWindowClose that is indistinguishable from quitting. Shutdown is then
            // driven only by the tray's Exit, which closes the window with its bypass flag set.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Checked before any window, folder scan or notification setup — a second instance
            // should exit as early and cheaply as possible, leaving the already-running one
            // completely undisturbed.
            _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
            _ownsSingleInstanceMutex = createdNew;

            if (!createdNew)
            {
                _ = ShowAlreadyRunningThenExit(desktop);
                return;
            }

            desktop.Exit += (_, _) =>
            {
                if (_ownsSingleInstanceMutex)
                {
                    _singleInstanceMutex?.ReleaseMutex();
                }

                _singleInstanceMutex?.Dispose();
            };

            // First run is "no library file yet". Welcome has to finish before MainWindow is
            // constructed, because MainViewModel reads that file in its constructor — showing it
            // afterwards would scan without the folders the user just added.
            if (!File.Exists(LibraryStore.GetDefaultPath()))
            {
                desktop.MainWindow = new WelcomeWindow();
                desktop.MainWindow.Closed += (_, _) =>
                {
                    var main = new MainWindow();
                    desktop.MainWindow = main;
                    main.Show();
                };
            }
            else
            {
                desktop.MainWindow = new MainWindow();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task ShowAlreadyRunningThenExit(IClassicDesktopStyleApplicationLifetime desktop)
    {
        await ConfirmDialog.ShowStandaloneMessageAsync("VST Manager", "VST Manager is already running.");
        desktop.Shutdown();
    }
}