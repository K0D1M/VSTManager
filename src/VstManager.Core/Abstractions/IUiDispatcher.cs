namespace VstManager.Core.Abstractions;

/// <summary>
/// Marshals work onto the UI thread. Background operations — cloud sync in particular — raise
/// events off-thread, and the bound properties they touch have to be set back on the UI thread.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>True when the caller is already on the UI thread.</summary>
    bool IsOnUiThread { get; }

    /// <summary>Queues work on the UI thread without waiting for it.</summary>
    void Post(Action action);

    /// <summary>Runs work on the UI thread and awaits its result.</summary>
    Task<T> InvokeAsync<T>(Func<T> func);

    /// <summary>Runs work on the UI thread and awaits its completion.</summary>
    Task InvokeAsync(Action action);
}
