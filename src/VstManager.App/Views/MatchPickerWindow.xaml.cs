using System.Diagnostics;
using System.Windows;
using VstManager.App.Services;
using VstManager.App.ViewModels;
using VstManager.Core.Services;

namespace VstManager.App.Views;

/// <summary>
/// Shown when auto-detect finds plausible matches but isn't confident enough to apply one
/// silently. The user's pick is returned via <see cref="SelectedInfo"/>; closing or choosing
/// "None of these" leaves it null so the caller changes nothing.
/// </summary>
public partial class MatchPickerWindow : Window
{
    private readonly List<MatchCandidateViewModel> _candidates;

    public KvrLookupResult? SelectedInfo { get; private set; }

    public MatchPickerWindow(string pluginName, IReadOnlyList<PluginInfoCandidate> candidates)
    {
        InitializeComponent();

        SubtitleText.Text = $"\"{pluginName}\" matched more than one entry online, or the match wasn't "
                            + "clear enough to apply automatically. Pick the correct one and its details "
                            + "will be filled in.";

        // Pre-select the best-scoring candidate so the common case is a single click, but
        // never apply it without the user confirming.
        _candidates = candidates
            .Select((candidate, index) => new MatchCandidateViewModel(candidate, isSelected: index == 0))
            .ToList();

        CandidateList.ItemsSource = _candidates;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => WindowSizing.FitToScreen(this);

    private void ViewPage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SelectedInfo = _candidates.FirstOrDefault(c => c.IsSelected)?.Info;
        DialogResult = SelectedInfo is not null;
        Close();
    }

    private void NoneOfThese_Click(object sender, RoutedEventArgs e)
    {
        SelectedInfo = null;
        DialogResult = false;
        Close();
    }
}
