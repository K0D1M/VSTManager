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

public class PluginInfo
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public required PluginFormat Format { get; set; }
    public PluginTag Tag { get; set; } = PluginTag.Unclassified;
}
