using CommunityToolkit.Mvvm.ComponentModel;
using VstManager.Core.Models;

namespace VstManager.Ui.ViewModels;

/// <summary>
/// Staging state for creating or editing one folder. A small deliberate dialog rather than
/// inline editing: personalising a folder is a single deliberate act, and a dialog can show a
/// live preview of the resulting row.
///
/// Unlike WPF's FolderEditWindow, the icon field takes a plain emoji directly with no
/// preset-vector-icon/font-switching machinery: that machinery exists in WPF only to work around
/// WPF's text renderer having no support for colour/bitmap glyph tables (COLR/CPAL, CBDT), which
/// makes most modern emoji render as tofu there. Avalonia's Skia-based text rendering does not
/// share that limitation — this project already shows emoji inline with text elsewhere (the
/// Hidden filter chip, Auto-Detect/Search buttons) with no font gymnastics, so none is needed here.
/// </summary>
public partial class FolderEditViewModel : ObservableObject
{
    /// <summary>
    /// A spread of distinguishable hues, matching WPF's FolderEditWindow palette so a folder made
    /// in one head looks the same offered again in the other. An instance property, not static:
    /// compiled bindings need something they can bind an instance to, and this is the DataContext
    /// the window's ItemsControl already targets.
    /// </summary>
    public IReadOnlyList<string> ColorPresets { get; } =
    [
        "#FF8A5CF6", "#FF3B82F6", "#FF06B6D4", "#FF10B981", "#FF84CC16", "#FFFBBF24",
        "#FFF97316", "#FFEF4444", "#FFEC4899", "#FFA855F7", "#FF8B949E", "#FF64748B"
    ];

    /// <summary>A few emoji covering common ways plugins get organised — pick one, or type any other.</summary>
    public IReadOnlyList<string> IconPresets { get; } =
    [
        "\U0001F4C1", "⭐", "\U0001F3B9", "\U0001F941", "\U0001F3A4", "\U0001F3B8",
        "\U0001F3AC", "\U0001F3AE", "❤️", "\U0001F525", "⚙️", "⬆️"
    ];

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _icon;

    [ObservableProperty]
    private string _colorHex;

    /// <summary>Set only when Save was rejected for a bad value, so the window can show it inline.</summary>
    [ObservableProperty]
    private string? _validationError;

    public FolderEditViewModel(PluginFolder folder)
    {
        IsNew = string.IsNullOrWhiteSpace(folder.Name);
        _name = folder.Name;
        _icon = folder.Icon;
        _colorHex = folder.ColorHex;
    }

    /// <summary>Drives the window title — "New Folder" versus "Edit — {name}".</summary>
    public bool IsNew { get; }

    /// <summary>The icon to show, falling back to a generic folder glyph, mirroring FolderNodeViewModel.</summary>
    public string PreviewIcon => string.IsNullOrWhiteSpace(Icon) ? "\U0001F4C1" : Icon;

    public string PreviewName => string.IsNullOrWhiteSpace(Name) ? "Untitled folder" : Name;

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(PreviewName));

    partial void OnIconChanged(string value) => OnPropertyChanged(nameof(PreviewIcon));

    /// <summary>
    /// Validates and, if valid, produces the folder to save. Returns null and sets
    /// <see cref="ValidationError"/> otherwise, so the window can show it without a dialog.
    /// </summary>
    public bool TryBuildResult(out string name, out string icon, out string colorHex)
    {
        name = Name.Trim();
        icon = Icon.Trim();
        colorHex = ColorHex.Trim();

        if (name.Length == 0)
        {
            ValidationError = "Give the folder a name.";
            return false;
        }

        if (!TryNormalizeColor(colorHex, out colorHex))
        {
            ValidationError = "That colour isn't valid. Use #RRGGBB or #AARRGGBB, or pick one above.";
            return false;
        }

        ValidationError = null;
        return true;
    }

    private static bool TryNormalizeColor(string text, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!Avalonia.Media.Color.TryParse(text, out var color))
        {
            return false;
        }

        normalized = color.ToString();
        return true;
    }
}
