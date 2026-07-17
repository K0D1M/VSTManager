using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.App.ViewModels;

/// <summary>
/// Backs the first-run Welcome window. Lets the user add extra scan folders before the very
/// first scan runs, so those folders are already known when MainViewModel loads afterward.
/// </summary>
public partial class WelcomeViewModel : ObservableObject
{
    private readonly LibraryStore _libraryStore = new();

    public IReadOnlyList<string> DefaultVst3Paths => ScanPathProvider.DefaultVst3Paths;
    public IReadOnlyList<string> DefaultVst2Paths => ScanPathProvider.DefaultVst2Paths;

    public ObservableCollection<string> CustomScanFolders { get; } = new();

    [RelayCommand]
    private void AddFolder(string? folder)
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
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        CustomScanFolders.Remove(folder);
    }

    public void Finish()
    {
        var data = new LibraryData
        {
            CustomScanFolders = CustomScanFolders.ToList()
        };

        _libraryStore.Save(data);
    }
}
