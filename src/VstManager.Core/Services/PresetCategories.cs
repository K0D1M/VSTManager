using VstManager.Core.Models;

namespace VstManager.Core.Services;

/// <summary>
/// The built-in category set — Instrument and Effect — seeded into a library the first time it's
/// opened after this feature lands. Ids are stable slugs, matching PresetTags' own convention, so
/// existing PluginInfo.CategoryId assignments survive a rename of the display name.
/// </summary>
public static class PresetCategories
{
    public const string Instrument = "instrument";
    public const string Effect = "effect";

    public static IReadOnlyList<CategoryDefinition> All { get; } = new List<CategoryDefinition>
    {
        new() { Id = Instrument, Name = "Instrument", Icon = "🎹", IsPreset = true },
        new() { Id = Effect,     Name = "Effect",     Icon = "🎛", IsPreset = true },
    };

    /// <summary>
    /// Adds any presets the library doesn't have yet, leaving existing ones (and any renames or
    /// re-iconing applied to them) untouched. Returns true when something was added, so the
    /// caller knows to save — same shape as PresetTags.EnsureSeeded.
    /// </summary>
    /// <summary>Instrument, Effect, then the user's own by SortOrder (name breaks ties, e.g. for
    /// categories created before SortOrder existed).</summary>
    public static List<CategoryDefinition> Ordered(IEnumerable<CategoryDefinition> categories)
    {
        int PresetRank(CategoryDefinition c) => c.Id switch
        {
            Instrument => 0,
            Effect => 1,
            _ => c.IsPreset ? 2 : 3
        };

        return categories
            .OrderBy(PresetRank)
            .ThenBy(c => c.SortOrder)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool EnsureSeeded(List<CategoryDefinition> categories)
    {
        var added = false;

        foreach (var preset in All)
        {
            if (categories.Any(c => string.Equals(c.Id, preset.Id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            categories.Add(new CategoryDefinition
            {
                Id = preset.Id,
                Name = preset.Name,
                Icon = preset.Icon,
                IsPreset = true
            });
            added = true;
        }

        return added;
    }
}
