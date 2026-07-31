using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Services;

namespace VstManager.App.ViewModels;

/// <summary>One row in the "which of these is it?" picker shown when auto-detect isn't sure.</summary>
public partial class MatchCandidateViewModel : ObservableObject
{
    public KvrLookupResult Info { get; }

    [ObservableProperty]
    private bool _isSelected;

    public MatchCandidateViewModel(PluginInfoCandidate candidate, bool isSelected)
    {
        Info = candidate.Info;
        Confidence = candidate.Confidence;
        _isSelected = isSelected;
    }

    public double Confidence { get; }

    public string Name => Info.ProductName;

    public string VendorText => string.IsNullOrWhiteSpace(Info.Vendor) ? "Unknown vendor" : Info.Vendor;

    public string VersionText => string.IsNullOrWhiteSpace(Info.LatestVersion)
        ? "No version listed"
        : $"Latest version {Info.LatestVersion}";

    public string? SourceUrl => Info.SourceUrl;

    public string ConfidenceText => Confidence switch
    {
        >= NameSimilarity.ConfidentThreshold => "Strong match",
        >= 0.6 => "Possible match",
        _ => "Weak match"
    };

    public bool HasSourceUrl => !string.IsNullOrWhiteSpace(Info.SourceUrl);
}
