using System.Text.Json;

namespace VstManager.Core.Models;

/// <summary>
/// A portable snapshot of everything VST Manager stores locally, so a user can carry their
/// plugin classifications, custom scan folders, and preferences to a fresh install. Each
/// section is stored as raw JSON (rather than a strongly-typed model) so an older export can
/// still be imported after the underlying file formats evolve.
/// </summary>
public class DataExportBundle
{
    public int FormatVersion { get; set; } = 1;
    public DateTime ExportedAt { get; set; }
    public JsonElement? Library { get; set; }
    public JsonElement? ExcludedFiles { get; set; }
    public JsonElement? ManualLogoOverrides { get; set; }
    public JsonElement? ManualMetadataOverrides { get; set; }
}
