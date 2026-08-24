using Microsoft.UI.Xaml.Controls;
using VstManager.WinUI.ViewModels;

namespace VstManager.WinUI.Views;

/// <summary>
/// Plugin details as a ContentDialog rather than a window: WinUI has no modal ShowDialog for
/// windows, and a dialog is the modern idiom for this anyway.
/// </summary>
public sealed partial class PluginDetailDialog : ContentDialog
{
    public PluginDisplayViewModel Plugin { get; }

    public PluginDetailDialog(PluginDisplayViewModel plugin)
    {
        Plugin = plugin;
        InitializeComponent();
        Title = plugin.Name;
    }

    public bool HasTags => Plugin.Tags.Count > 0;

    /// <summary>
    /// x:Bind calls this directly — cheaper than a converter for a single formatting rule.
    /// Deliberately an instance method: x:Bind function binding resolves against the page
    /// instance, and a static here fails to compile with CS0176.
    /// </summary>
    public string VersionOrDash(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "—" : version!;
}
