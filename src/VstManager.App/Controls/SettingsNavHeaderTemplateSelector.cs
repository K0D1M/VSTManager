using System.Windows;
using System.Windows.Controls;

namespace VstManager.App.Controls;

/// <summary>
/// Chooses the header layout for a tab styled with SettingsNavItem. Tabs whose Header is a
/// <see cref="SettingsTabHeader"/> get the icon+label template; tabs with a plain string header
/// (e.g. the plugin detail window's "Details" / "Files") fall back to WPF's default text
/// rendering.
///
/// Without this, applying the icon+label template unconditionally binds .Icon/.Text on a plain
/// string — which resolve to nothing — and the tab label silently disappears.
/// </summary>
public class SettingsNavHeaderTemplateSelector : DataTemplateSelector
{
    /// <summary>The icon+label template, used only when the header is a SettingsTabHeader.</summary>
    public DataTemplate? IconTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is SettingsTabHeader ? IconTemplate : null;
}
