using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using VstManager.Core.Services;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Shown when a lookup turns up several plausible products and the app should not guess — for
/// example a name that matches both "1176" and "UADx 1176". Nothing is applied unless the user
/// picks: dismissing the window leaves the plugin exactly as it was.
/// </summary>
public partial class MatchPickerWindow : Window
{
    private readonly IReadOnlyList<MatchCandidateViewModel> _candidates;

    public MatchPickerWindow()
    {
        InitializeComponent();
        _candidates = [];
    }

    public MatchPickerWindow(string pluginName, IReadOnlyList<PluginInfoCandidate> candidates) : this()
    {
        SubtitleText.Text = $"Found more than one possible match for “{pluginName}”. "
                            + "Pick the one that matches what you have installed.";

        // The list arrives best-first, so pre-select the top row: the common case is one click to
        // confirm. It is never auto-applied — a wrong "confident" match is exactly what this
        // window exists to let the user correct.
        _candidates = candidates
            .Select((candidate, index) => new MatchCandidateViewModel(candidate, index == 0))
            .ToList();

        CandidateList.ItemsSource = _candidates;
    }

    /// <summary>
    /// Shows the picker and resolves to the chosen product, or null if the user declined. Null is
    /// the "change nothing" answer, so dismissing the window is always safe.
    /// </summary>
    public static async Task<KvrLookupResult?> PickAsync(
        Window owner, string pluginName, IReadOnlyList<PluginInfoCandidate> candidates)
    {
        var dialog = new MatchPickerWindow(pluginName, candidates);
        return await dialog.ShowDialog<KvrLookupResult?>(owner);
    }

    private void Apply_Click(object? sender, RoutedEventArgs e) =>
        Close(_candidates.FirstOrDefault(c => c.IsSelected)?.Info);

    private void NoneOfThese_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void ViewPage_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // No browser, or a URL the shell refuses — not worth interrupting the choice over.
            }
        }
    }
}
