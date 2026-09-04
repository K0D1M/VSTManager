namespace VstManager.App.Services;

/// <summary>
/// The vector-icon presets offered when personalising a folder, and the means to tell one of
/// them apart from a free-typed emoji later.
///
/// A <see cref="Core.Models.PluginFolder.Icon"/> ends up holding either kind of glyph, and the two
/// need different fonts to render at all: WPF's text renderer has no support for colour/bitmap
/// glyph tables (COLR/CPAL, CBDT), so most of Segoe UI Emoji's modern pictographs show as tofu
/// regardless of machine — a framework limitation, not a missing or wrong font. These presets are
/// plain vector outline glyphs from Segoe Fluent Icons / Segoe MDL2 Assets instead, which WPF
/// renders exactly like normal text. Anywhere a folder's icon is displayed has to pick the right
/// font per-instance via <see cref="IsPreset"/> — mixing an icon-font glyph and ordinary text in
/// one run under one FontFamily renders one of the two as tofu, so icon and label can never share
/// a single text run once a preset is involved (see MainWindow.xaml's folder row, which keeps
/// them as separate TextBlocks for exactly this reason).
/// </summary>
public static class FolderIconGlyphs
{
    public static readonly IReadOnlyList<string> Presets =
    [
        "", // Folder (E8B7)
        "", // FavoriteStar (E734)
        "", // FavoriteStarFill (E735)
        "", // Tag (E8EC)
        "", // Flag (E7C1)
        "", // MusicNote (EC4F)
        "", // Home (E80F)
        "", // Setting (E713)
        "", // Contact (E77B)
        "", // Edit (E70F)
        "", // Delete (E74D)
        ""  // Info (E946)
    ];

    public static bool IsPreset(string? icon) =>
        !string.IsNullOrEmpty(icon) && Presets.Contains(icon);
}
