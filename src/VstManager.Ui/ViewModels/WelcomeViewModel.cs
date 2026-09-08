using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.Ui.ViewModels;

/// <summary>
/// Backs the first-run Welcome window. Lets the user add extra scan folders before the very
/// first scan runs, so those folders are already known when the library loads afterwards.
/// </summary>
public partial class WelcomeViewModel : ObservableObject
{
    private readonly LibraryStore _libraryStore = new();

    public IReadOnlyList<string> DefaultVst3Paths => ScanPathProvider.DefaultVst3Paths;

    public IReadOnlyList<string> DefaultVst2Paths => ScanPathProvider.DefaultVst2Paths;

    public ObservableCollection<string> CustomScanFolders { get; } = new();

    public void AddFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        if (!CustomScanFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            CustomScanFolders.Add(folder);
        }
    }

    [RelayCommand]
    private void RemoveFolder(string? folder)
    {
        if (!string.IsNullOrWhiteSpace(folder))
        {
            CustomScanFolders.Remove(folder);
        }
    }

    /// <summary>
    /// Writes the starting library so the first scan already knows these folders. Creating the
    /// file is also what marks first-run as done — its absence is the signal to show this window.
    /// </summary>
    public void Finish() =>
        _libraryStore.Save(new LibraryData { CustomScanFolders = CustomScanFolders.ToList() });
}
