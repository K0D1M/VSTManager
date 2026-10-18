using System.ComponentModel;
using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.Ui.ViewModels;

/// <summary>
/// A product bundle (e.g. SWAM's instruments) shown as one card. Data stays per plugin: the card
/// is built from a synthetic display item holding every member's installs, so favourite, hidden,
/// category and Legit/Cracked summaries come out of the normal card logic, and any action on the
/// card is expanded to the members by MainViewModel.ResolveTargets. Versions stay per member.
/// </summary>
public sealed class BundleCardViewModel : PluginCardViewModel
{
    public BundleCardViewModel(PluginBundle bundle, IReadOnlyList<PluginCardViewModel> members)
        : base(BuildItem(bundle, members))
    {
        Members = members;
        IsManual = bundle.IsManual;

        foreach (var member in members)
        {
            member.PropertyChanged += OnMemberChanged;
        }

        RefreshFromItem();
        Logo = members.FirstOrDefault(m => m.Logo is not null)?.Logo;
    }

    /// <summary>The real plugins inside, sorted by name.</summary>
    public IReadOnlyList<PluginCardViewModel> Members { get; private set; } = [];

    /// <summary>Made by hand ("Merge into Bundle") rather than by the automatic name rule.</summary>
    public bool IsManual { get; }

    public override bool IsBundle => true;

    public int OutdatedCount => Members.Count(m => m.IsInstalled && m.ShowOutdatedBadge);

    public override string SummaryLine => string.IsNullOrEmpty(Formats)
        ? $"{Members.Count} plugins"
        : $"{Members.Count} plugins · {Formats}";

    public override string? UpdateTooltip => OutdatedCount switch
    {
        0 => null,
        1 => "1 plugin in this bundle has an update",
        var n => $"{n} plugins in this bundle have updates"
    };

    public override void RefreshFromItem()
    {
        // Tags are per member; the bundle shows the union (manual first, as each member does).
        if (Members.Count > 0)
        {
            Item.Tags = Members.SelectMany(m => m.Item.Tags)
                .GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            Item.AutoTagIds = new HashSet<string>(
                Members.SelectMany(m => m.Item.AutoTagIds), StringComparer.OrdinalIgnoreCase);
        }

        base.RefreshFromItem();

        // One version for 38 instruments means nothing; the bundle is "outdated" if any member is.
        IsOutdated = OutdatedCount > 0;
        OnPropertyChanged(nameof(OutdatedCount));
        OnPropertyChanged(nameof(SummaryLine));
        OnPropertyChanged(nameof(UpdateTooltip));
    }

    /// <summary>Stops listening to the members when the bundle is rebuilt or split.</summary>
    public void Detach()
    {
        foreach (var member in Members)
        {
            member.PropertyChanged -= OnMemberChanged;
        }
    }

    private void OnMemberChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Logo) && Logo is null && sender is PluginCardViewModel { Logo: { } logo })
        {
            Logo = logo;
        }
    }

    private static PluginDisplayItem BuildItem(PluginBundle bundle, IReadOnlyList<PluginCardViewModel> members) => new()
    {
        Name = bundle.Name,
        Vendor = bundle.Vendor,
        BaseName = bundle.Key,
        Installs = members.SelectMany(m => m.Installs).ToList()
    };
}
