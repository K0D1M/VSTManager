using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VstManager.Core.Models;
using VstManager.Core.Services;
using VstManager.WinUI.Services;

namespace VstManager.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        IsolatedData.EnsureSeeded();

        var library = new LibraryStore(IsolatedData.Library).Load();
        PresetTags.EnsureSeeded(library.Tags);

        Tags = library.Tags;
        ScanFolders = new ScanPathProvider()
            .GetVst3Paths(library.CustomScanFolders)
            .Concat(new ScanPathProvider().GetVst2Paths(library.CustomScanFolders))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        InitializeComponent();
    }

    public IReadOnlyList<TagDefinition> Tags { get; }

    public IReadOnlyList<string> ScanFolders { get; }

    public string DataFolder => IsolatedData.Folder;

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: string tag } })
        {
            return;
        }

        // Setting RequestedTheme on the root element re-resolves every ThemeResource beneath it —
        // WinUI's declarative equivalent of the WPF head's merged-dictionary swap.
        if (Content is FrameworkElement root && Enum.TryParse<ElementTheme>(tag, out var theme))
        {
            root.RequestedTheme = theme;
        }
    }
}
