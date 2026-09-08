using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using VstManager.Core.Services;
using VstManager.Ui.Views;

namespace VstManager.Ui;

public partial class App : Application
{
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
}