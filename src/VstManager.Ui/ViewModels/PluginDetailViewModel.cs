using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VstManager.Core.Models;
using VstManager.Core.Services;

namespace VstManager.Ui.ViewModels;

/// <summary>
/// DataContext for the detail window. Bundles the plugin being viewed, the draft edit form and
/// the tag picker so the window binds to plain paths, instead of the RelativeSource chains the
/// WPF version needs to reach its code-behind's Form property.
/// </summary>
public partial class PluginDetailViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public PluginCardViewModel Plugin { get; }

    public PluginEditFormViewModel Form { get; }

    /// <summary>The plugins inside, when this window shows a bundle; empty otherwise.</summary>
    public IReadOnlyList<PluginCardViewModel> BundleMembers => (Plugin as BundleCardViewModel)?.Members ?? [];

    public IReadOnlyList<TagPickerItemViewModel> TagItems { get; }

    public ObservableCollection<CategoryDefinition> AvailableCategories => _main.AvailableCategories;

    /// <summary>Raised when Save has committed everything and the window should close.</summary>
    public event EventHandler? CloseRequested;

    public PluginDetailViewModel(MainViewModel main, PluginCardViewModel plugin)
    {
        _main = main;
        Plugin = plugin;
        Form = new PluginEditFormViewModel(plugin);
        TagItems = main.AvailableTags
            .Select(tag => new TagPickerItemViewModel(main, plugin, tag))
            .ToList();

        plugin.PropertyChanged += OnPluginChanged;
    }

    public string IgnoreLabel => Plugin.IgnoreVersionCheck ? "Show Update Badge" : "Ignore Version Updates";

    private void OnPluginChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginCardViewModel.IgnoreVersionCheck))
        {
            OnPropertyChanged(nameof(IgnoreLabel));
        }
    }

    [RelayCommand]
    private void ToggleIgnoreVersionCheck() => _main.ToggleIgnoreVersionCheckCommand.Execute(Plugin);

    /// <summary>
    /// Re-reads the installed version on demand, staging it into the form for the user to keep
    /// with Save. Walks the same chain as a refresh — each copy's file metadata, then the Windows
    /// uninstall registry — and reports which one answered.
    /// </summary>
    [RelayCommand]
    private async Task DetectCurrentVersionAsync()
    {
        Form.IsDetectingCurrentVersion = true;
        Form.StatusText = "Reading the installed version…";

        try
        {
            var result = await Task.Run(() => _main.DetectInstalledVersion(Plugin));

            if (result.Version is null)
            {
                Form.StatusText = $"No version found in {result.SourceDescription}. Some plugins "
                                  + "never record one — you can type it in yourself.";
                return;
            }

            var previous = Form.CurrentVersion?.Trim();
            Form.CurrentVersion = result.Version;
            Form.StatusText = string.Equals(previous, result.Version, StringComparison.OrdinalIgnoreCase)
                ? $"Confirmed version {result.Version} from {result.SourceDescription}."
                : $"Found version {result.Version} in {result.SourceDescription}. Click Save to keep it.";
        }
        finally
        {
            Form.IsDetectingCurrentVersion = false;
        }
    }

    /// <summary>
    /// Commits the draft. Returns false, without closing, when the name is empty — the window
    /// reports that, since this view model stays dialog-free. Artwork can also fail (an
    /// undecodable image, a dead URL); that is reported through <see cref="SaveWarning"/> with
    /// everything else already saved, rather than discarding the whole edit.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        SaveWarning = null;

        if (string.IsNullOrWhiteSpace(Form.Name))
        {
            return false;
        }

        var name = Form.Name.Trim();
        var vendor = Form.Vendor?.Trim() ?? string.Empty;

        if (name != Plugin.Name || vendor != (Plugin.Vendor ?? string.Empty))
        {
            _main.ApplyMetadataOverride(Plugin, name, vendor);
        }

        // Versions are recorded as a manual override, which permanently outranks later online
        // refreshes — so only when the user actually changed one. Writing on every Save would
        // silently freeze whatever happened to be on screen.
        var current = Form.CurrentVersion?.Trim();
        var latest = Form.LatestVersion?.Trim();
        var currentChanged = !string.Equals(current ?? string.Empty, Plugin.CurrentVersion ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        var latestChanged = !string.Equals(latest ?? string.Empty, Plugin.LatestVersion ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        if (currentChanged || latestChanged)
        {
            _main.SetVersions(Plugin, current, latest, setCurrent: currentChanged, setLatest: latestChanged);
        }

        // Category is the newer, more general control and takes precedence when it changed —
        // SetCategory writes Kind too for the two presets, so applying both this and the Type
        // segment below in the same Save would be redundant at best. The Type segment's own
        // MarkInstrument/MarkEffectCommand path only runs when Category itself didn't change,
        // covering the case where the user only touched Type (Category still whatever it was).
        if (Form.SelectedCategoryId != Plugin.CategoryId)
        {
            var category = _main.AvailableCategories.FirstOrDefault(c => c.Id == Form.SelectedCategoryId);
            _main.SetCategory(Plugin, category);
        }
        else if (Form.Kind != Plugin.Kind)
        {
            if (Form.Kind == PluginKind.Instrument)
            {
                _main.MarkInstrumentCommand.Execute(Plugin);
            }
            else if (Form.Kind == PluginKind.Effect)
            {
                _main.MarkEffectCommand.Execute(Plugin);
            }
        }

#if !NO_LICENSE_STATUS
        if (Form.SelectedTag == PluginTag.Legit)
        {
            _main.MarkLegitCommand.Execute(Plugin);
        }
        else if (Form.SelectedTag == PluginTag.Cracked)
        {
            _main.MarkCrackedCommand.Execute(Plugin);
        }
#endif

        // Artwork last, and a local file wins: choosing one clears the URL, so both being set
        // means the file was picked most recently.
        if (!string.IsNullOrWhiteSpace(Form.LocalLogoFilePath))
        {
            if (!await _main.SetLogoFromLocalFileAsync(Plugin, Form.LocalLogoFilePath))
            {
                SaveWarning = "Couldn't save the image. The rest of your changes were saved.";
            }
        }
        else if (Form.IsLogoPreviewValid && !string.IsNullOrWhiteSpace(Form.LogoUrl))
        {
            if (!await _main.FixLogoAsync(Plugin, Form.LogoUrl.Trim()))
            {
                SaveWarning = "Couldn't save the artwork. The rest of your changes were saved.";
            }
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Set when Save succeeded overall but artwork could not be applied.</summary>
    public string? SaveWarning { get; private set; }

    public Task<bool> ExcludePathAsync(string path) => _main.ExcludePathFromScanAsync(path);

    public Task<bool> MarkAsNotAPluginAsync() => _main.MarkAsNotAPluginAsync(Plugin);

    // ---- online lookup -----------------------------------------------------
    // Everything here stages into Form and writes nothing; the user commits with Save. That is
    // what makes a wrong match harmless — closing the window discards it.

    /// <summary>
    /// Asked when a lookup finds several plausible products. The window owns the picker dialog,
    /// so this view model can stay dialog-free like the rest; returning null means "change
    /// nothing", which is also what a missing handler resolves to.
    /// </summary>
    public Func<IReadOnlyList<PluginInfoCandidate>, Task<KvrLookupResult?>>? CandidateChooser { get; set; }

    /// <summary>Loads a preview image for a URL; the window owns the decoding, so it supplies this.</summary>
    public Func<string, Task<bool>>? LogoPreviewLoader { get; set; }

    [RelayCommand]
    private async Task AutoDetectAsync()
    {
        Form.IsAutoDetecting = true;
        Form.AutoDetectStatusText = "Looking this plugin up…";

        try
        {
            var result = await _main.PreviewAutoDetectAsync(Plugin);
            var messages = new List<string>();

            if (result.DetectedCurrentVersion is not null
                && !string.Equals(result.DetectedCurrentVersion, Form.CurrentVersion, StringComparison.OrdinalIgnoreCase))
            {
                Form.CurrentVersion = result.DetectedCurrentVersion;
                messages.Add($"installed version {result.DetectedCurrentVersion}");
            }

            var chosen = result.WebLookupResult;

            if (result.NeedsUserChoice && result.WebCandidates.Count > 0 && CandidateChooser is not null)
            {
                chosen = await CandidateChooser(result.WebCandidates);
            }

            if (chosen is not null)
            {
                messages.AddRange(await ApplyFetchedInfoAsync(chosen));
            }

            Form.AutoDetectStatusText = messages.Count > 0
                ? $"Found {string.Join(", ", messages)}. Review above, then click Save."
                : "Nothing new found. Try Search Manually if the name on disk differs from the product name.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Form.AutoDetectStatusText = "Couldn't reach the plugin database. Check your connection and try again.";
        }
        finally
        {
            Form.IsAutoDetecting = false;
        }
    }

    /// <summary>
    /// Looks up the name the user typed and always shows the picker, even for a single strong
    /// hit — the whole point is correcting a match Auto-Detect got wrong, so it must never
    /// silently decide again.
    /// </summary>
    [RelayCommand]
    private async Task SearchManuallyAsync()
    {
        var name = Form.Name?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Form.AutoDetectStatusText = "Enter a name above first, then search.";
            return;
        }

        Form.IsSearchingManually = true;
        Form.AutoDetectStatusText = $"Searching for “{name}”…";

        try
        {
            var candidates = await _main.SearchCandidatesAsync(name, Form.Vendor?.Trim());

            if (candidates.Count == 0)
            {
                Form.AutoDetectStatusText = "No matches found. Try a shorter name, or paste the "
                                            + "product page address on the Web Info tab.";
                return;
            }

            var chosen = CandidateChooser is null ? null : await CandidateChooser(candidates);

            if (chosen is null)
            {
                Form.AutoDetectStatusText = "Nothing applied.";
                return;
            }

            var messages = await ApplyFetchedInfoAsync(chosen);
            Form.AutoDetectStatusText = $"Filled in {string.Join(", ", messages)}. Review above, then click Save.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Form.AutoDetectStatusText = "Couldn't reach the plugin database. Check your connection and try again.";
        }
        finally
        {
            Form.IsSearchingManually = false;
        }
    }

    /// <summary>Reads a product page the user pasted — the fallback when searching can't find it.</summary>
    [RelayCommand]
    private async Task FetchInfoAsync()
    {
        var url = Form.InfoUrl?.Trim();

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            Form.LogoStatusText = "Paste a full web address first (starting with https://).";
            return;
        }

        Form.IsFetchingInfo = true;
        Form.LogoStatusText = "Reading that page…";

        try
        {
            var info = await _main.FetchInfoFromUrlAsync(url);

            if (info is null)
            {
                Form.LogoStatusText = "Couldn't read anything useful from that page.";
                return;
            }

            var messages = await ApplyFetchedInfoAsync(info);
            Form.LogoStatusText = $"Filled in {string.Join(", ", messages)}. Review, then click Save.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            Form.LogoStatusText = "Couldn't reach that page. Check your connection and try again.";
        }
        finally
        {
            Form.IsFetchingInfo = false;
        }
    }

    /// <summary>
    /// Stages a looked-up product into the form. Only fills fields the source actually supplied,
    /// so a partial result never blanks a good value the user already has.
    /// </summary>
    private async Task<List<string>> ApplyFetchedInfoAsync(KvrLookupResult info)
    {
        var applied = new List<string>();

        if (!string.IsNullOrWhiteSpace(info.ProductName) && info.ProductName != Form.Name)
        {
            Form.Name = info.ProductName;
            applied.Add("name");
        }

        if (!string.IsNullOrWhiteSpace(info.Vendor) && info.Vendor != Form.Vendor)
        {
            Form.Vendor = info.Vendor;
            applied.Add("vendor");
        }

        if (!string.IsNullOrWhiteSpace(info.LatestVersion) && info.LatestVersion != Form.LatestVersion)
        {
            Form.LatestVersion = info.LatestVersion;
            applied.Add($"latest version {info.LatestVersion}");
        }

        if (!string.IsNullOrWhiteSpace(info.SourceUrl))
        {
            Form.InfoUrl = info.SourceUrl;
        }

        if (!string.IsNullOrWhiteSpace(info.LogoUrl) && LogoPreviewLoader is not null)
        {
            Form.LogoUrl = info.LogoUrl;

            // A local file and a URL are mutually exclusive; the newest choice wins.
            Form.LocalLogoFilePath = null;

            if (await LogoPreviewLoader(info.LogoUrl))
            {
                applied.Add("artwork");
            }
        }

        return applied;
    }
}
