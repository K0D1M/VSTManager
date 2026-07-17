using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;

namespace VstManager.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Something went wrong and the action couldn't be completed:\n\n{e.Exception.Message}",
            "VST Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }
}

