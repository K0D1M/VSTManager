using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VstManager.App.Services;
using VstManager.Core.Models;

namespace VstManager.App.Views;

/// <summary>
/// Name, icon and colour for one folder. Deliberately a small modal rather than inline editing:
/// creating a folder and personalising it is a single deliberate act, and a dialog can show a
/// live preview of the resulting row.
/// </summary>
public partial class FolderEditWindow : Window
{
    /// <summary>
    /// A spread of distinguishable hues. Matches the palette feel of the tag colours without
    /// requiring a full colour picker for what is usually a quick choice.
    /// </summary>
    private static readonly string[] ColorPresets =
    [
        "#FF8A5CF6", "#FF3B82F6", "#FF06B6D4", "#FF10B981", "#FF84CC16", "#FFFBBF24",
        "#FFF97316", "#FFEF4444", "#FFEC4899", "#FFA855F7", "#FF8B949E", "#FF64748B"
    ];

    public FolderEditWindow(PluginFolder folder)
    {
        InitializeComponent();

        Title = string.IsNullOrWhiteSpace(folder.Name) ? "New Folder" : $"Edit — {folder.Name}";

        IconChoices.ItemsSource = FolderIconGlyphs.Presets;
        ColorChoices.ItemsSource = ColorPresets
            .Select(hex => (Color)ColorConverter.ConvertFromString(hex))
            .ToList();

        NameBox.Text = folder.Name;
        IconBox.Text = folder.Icon;
        ColorBox.Text = folder.ColorHex;

        UpdatePreview();
        Loaded += (_, _) => { NameBox.SelectAll(); NameBox.Focus(); };
    }

    /// <summary>Set when the user saves; the caller reads these back onto the folder.</summary>
    public string FolderName { get; private set; } = string.Empty;

    public string FolderIcon { get; private set; } = string.Empty;

    public string FolderColorHex { get; private set; } = string.Empty;

    private void Field_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void IconChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string icon })
        {
            IconBox.Text = icon;
        }
    }

    private void ColorChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Color color })
        {
            ColorBox.Text = color.ToString();
        }
    }

    /// <summary>
    /// Mirrors the fields into the preview row. An unparseable colour leaves the last good swatch
    /// in place rather than throwing while the user is mid-type.
    /// </summary>
    private void UpdatePreview()
    {
        var icon = string.IsNullOrWhiteSpace(IconBox.Text) ? "\U0001F4C1" : IconBox.Text;
        PreviewName.Text = string.IsNullOrWhiteSpace(NameBox.Text) ? "Untitled folder" : NameBox.Text;
        PreviewIcon.Text = icon;

        // The current icon is either one of the vector presets (needs the icon font, or it
        // shows as tofu) or free-typed text -- an emoji, or just a letter -- which wants the
        // normal emoji/text font instead.
        PreviewIcon.FontFamily = (FontFamily)FindResource(
            FolderIconGlyphs.IsPreset(icon) ? "IconGlyphFontFamily" : "EmojiFontFamily");

        if (TryParseColor(ColorBox.Text, out var color))
        {
            PreviewSwatch.Background = new SolidColorBrush(color);
        }
    }

    private static bool TryParseColor(string? text, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            if (ColorConverter.ConvertFromString(text.Trim()) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
            // Half-typed hex ("#FF8A") is expected while editing — not worth surfacing.
        }

        return false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Give the folder a name.", "Folder",
                MessageBoxButton.OK, MessageBoxImage.Information);
            NameBox.Focus();
            return;
        }

        if (!TryParseColor(ColorBox.Text, out var color))
        {
            MessageBox.Show(this, "That colour isn't valid. Use #RRGGBB or #AARRGGBB, or pick one above.",
                "Folder", MessageBoxButton.OK, MessageBoxImage.Information);
            ColorBox.Focus();
            return;
        }

        FolderName = name;
        FolderIcon = IconBox.Text.Trim();
        FolderColorHex = color.ToString();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
