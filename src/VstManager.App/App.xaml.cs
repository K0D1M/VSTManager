using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Threading;
using VstManager.App.Services;

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

        // Registered as a class handler so every ScrollViewer gets smooth wheel scrolling,
        // including ones generated inside control templates.
        SmoothScroll.EnableGlobally();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Something went wrong and the action couldn't be completed:\n\n{Describe(e.Exception)}",
            "VST Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
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

