namespace VstManager.Core.Models;

public enum PluginFormat
{
    Vst2,
    Vst3
}

public enum PluginTag
{
    Unclassified,
    Legit,
    Cracked
}

public enum PluginTagSummary
{
    Unclassified,
    Legit,
    Cracked,
    Both
}

public enum PluginKind
{
    Unclassified,
    Instrument,
    Effect
}

public class PluginInfo
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public required PluginFormat Format { get; set; }
    public PluginTag Tag { get; set; } = PluginTag.Unclassified;
    public PluginKind Kind { get; set; } = PluginKind.Unclassified;
    public string? CurrentVersion { get; set; }
    public string? LatestVersion { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }
}
