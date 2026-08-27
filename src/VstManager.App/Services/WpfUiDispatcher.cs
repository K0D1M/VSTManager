using System.Windows;
using VstManager.Core.Abstractions;

namespace VstManager.App.Services;

/// <summary>
/// WPF implementation of <see cref="IUiDispatcher"/>. Every member tolerates a null
/// <c>Application.Current</c> — it is null in unit tests and during shutdown, and the original
/// code guarded for it with <c>?.</c> at each call site.
/// </summary>
public class WpfUiDispatcher : IUiDispatcher
{
    public bool IsOnUiThread => Application.Current?.Dispatcher.CheckAccess() ?? false;

    public void Post(Action action) => Application.Current?.Dispatcher.InvokeAsync(action);

    public async Task<T> InvokeAsync<T>(Func<T> func)
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null ? func() : await dispatcher.InvokeAsync(func);
    }

    public async Task InvokeAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }
}
