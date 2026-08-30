namespace VstManager.Core.Models;

/// <summary>
/// A user's correction to what the app worked out on its own. Manual corrections are absolute:
/// once set, no scan, lookup or metadata refresh may overwrite the fields recorded here.
/// </summary>
public class ManualMetadataOverride
{
    public string? Name { get; set; }
    public string? Vendor { get; set; }

    /// <summary>
    /// The installed version, when the user has corrected it. Null means "not corrected" — the
    /// detected value is used and may be re-detected freely.
    /// </summary>
    public string? CurrentVersion { get; set; }

    /// <summary>
    /// The latest-available version, when the user has corrected it.
    ///
    /// This is the field that matters most in practice: KVR lookup matches by name, and near-name
    /// collisions between genuinely different products are common (UADx 1176, the native plugin,
    /// versus UAD 1176, the DSP bundle whose version tracks the whole UAD suite). When the user
    /// fixes such a mismatch, a later refresh must not silently undo it.
    /// </summary>
    public string? LatestVersion { get; set; }
}
