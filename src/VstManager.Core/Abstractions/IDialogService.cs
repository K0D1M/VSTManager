namespace VstManager.Core.Abstractions;

/// <summary>
/// Severity of a message shown to the user. Maps to whatever the host UI uses to convey it —
/// a WPF <c>MessageBoxImage</c>, an Avalonia dialog icon, and so on.
/// </summary>
public enum DialogSeverity
{
    Information,
    Question,
    Warning,
    Error
}

/// <summary>
/// User-facing dialogs, abstracted away from any one UI framework so the view models that raise
/// them can be shared between the WPF and Avalonia heads.
///
/// Deliberately narrow: the app only ever shows a message or asks a yes/no question, so those are
/// the only two shapes offered. Anything more elaborate belongs in a purpose-built window rather
/// than a general-purpose dialog API.
/// </summary>
public interface IDialogService
{
    /// <summary>Shows a message with a single dismiss button.</summary>
    void ShowMessage(string message, string title, DialogSeverity severity = DialogSeverity.Information);

    /// <summary>Asks a yes/no question. Returns true only for an explicit yes.</summary>
    bool Confirm(string message, string title, DialogSeverity severity = DialogSeverity.Question);

    /// <summary>Picks a single existing folder, or null when the user cancels.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>
    /// Picks a single existing file, or null when the user cancels. <paramref name="filter"/> uses
    /// the Win32 "Label|*.ext" convention; implementations translate as needed.
    /// </summary>
    Task<string?> PickFileAsync(string title, string filter);

    /// <summary>Picks a destination path to write to, or null when the user cancels.</summary>
    Task<string?> PickSaveFileAsync(string title, string filter, string defaultFileName);
}
