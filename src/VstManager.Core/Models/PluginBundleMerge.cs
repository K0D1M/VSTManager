namespace VstManager.Core.Models;

/// <summary>A bundle the user put together by hand: a name and its members' BaseNames.</summary>
public class PluginBundleMerge
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Members { get; set; } = new();
}
