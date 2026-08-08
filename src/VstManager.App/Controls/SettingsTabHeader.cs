namespace VstManager.App.Controls;

/// <summary>
/// A settings tab's header: which icon geometry to show (a resource key looked up by
/// <see cref="Converters.ResourceKeyToGeometryConverter"/>) and the label text. Used as
/// TabItem.Header so every tab is a one-line attribute instead of a repeated icon+text
/// StackPanel — see SettingsNavItemHeader in Styles.xaml.
/// </summary>
public sealed class SettingsTabHeader
{
    public required string Icon { get; init; }
    public required string Text { get; init; }

    /// <summary>
    /// WPF falls back to this whenever something needs the header as plain text instead of
    /// visual content — UI Automation's Name property (what a screen reader announces), tooltip
    /// fallback text, and so on. Without it, assistive tech would read the CLR type name instead
    /// of the tab's label.
    /// </summary>
    public override string ToString() => Text;
}
