using Avalonia;
using System;
using System.IO;
using System.Reflection;

namespace VstManager.Ui;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // VstManager.Ui.WindowsNotifications and its dependencies (DesktopNotifications.Windows,
        // Microsoft.Toolkit.Uwp.Notifications, Microsoft.Windows.SDK.NET, WinRT.Runtime, ...) are
        // brought in as plain copied files (see VstManager.Ui.csproj), not real project/package
        // references, since this project must stay on plain net10.0 for macOS. That means none of
        // them are listed in VstManager.Ui.deps.json, so the default assembly-resolution probe
        // (which relies on deps.json for anything not matching the exact requested version) can
        // fail a strong-named load even though the exact-named file sits right next to the .exe —
        // confirmed by a FileNotFoundException naming an assembly version that exists nowhere on
        // disk. This handler is the fallback: for any of that specific set of assemblies, load
        // whatever copy is actually in the base directory regardless of the requested version.
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
