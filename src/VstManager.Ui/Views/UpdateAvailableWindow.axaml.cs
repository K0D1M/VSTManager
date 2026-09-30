using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using VstManager.Ui.ViewModels;

namespace VstManager.Ui.Views;

/// <summary>
/// Offers an app update: what's new, then an in-window download with progress, then hands over to
/// the installer. Opened at launch (unless the version was skipped) and from Settings → About.
/// </summary>
public partial class UpdateAvailableWindow : Window
{
    private readonly MainViewModel? _vm;
    private CancellationTokenSource? _download;

    public UpdateAvailableWindow() => InitializeComponent();

    private UpdateAvailableWindow(MainViewModel vm, bool offerSkip) : this()
    {
        _vm = vm;

        TitleText.Text = $"VST Manager {vm.LatestAppVersion} is available";
        CurrentVersionText.Text = MainViewModel.CurrentVersion;
        NewVersionText.Text = vm.LatestAppVersion;
        SkipButton.IsVisible = offerSkip;

        if (vm.HasAppInstaller)
        {
            var size = vm.AppInstallerSize is { } bytes ? $" · {bytes / 1048576.0:0.#} MB" : string.Empty;
            InstallerText.Text = $"Installer: {Services.UpdateChecker.InstallerAssetName}{size}";
        }
        else
        {
            InstallerText.Text = "No installer for this platform in this release.";
            CloseNoteText.IsVisible = false;
            InstallIcon.IsVisible = false;
            InstallButtonText.Text = "Open download page";
        }

        FillNotes(vm.AppReleaseNotes);
        Closing += (_, e) =>
        {
            // Closing mid-download cancels it rather than leaving it running headless.
            _download?.Cancel();
        };
    }

    public static Task ShowAsync(Window owner, MainViewModel vm, bool offerSkip) =>
        new UpdateAvailableWindow(vm, offerSkip).ShowDialog(owner);

    /// <summary>
    /// GitHub release notes are markdown; shown as readable text rather than rendered, keeping
    /// headings and bullets and dropping the markup around them.
    /// </summary>
    private void FillNotes(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            NotesPanel.Children.Add(Line("No release notes were published for this version.", muted: true));
            return;
        }

        foreach (var raw in markdown.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            line = Regex.Replace(line, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
            line = line.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty);

            if (line.StartsWith('#'))
            {
                var heading = Line(line.TrimStart('#', ' '), muted: false);
                heading.FontWeight = FontWeight.SemiBold;
                heading.Margin = new Avalonia.Thickness(0, NotesPanel.Children.Count == 0 ? 0 : 8, 0, 0);
                NotesPanel.Children.Add(heading);
            }
            else if (line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("+ "))
            {
                NotesPanel.Children.Add(Line("•  " + line[2..].Trim(), muted: true));
            }
            else
            {
                NotesPanel.Children.Add(Line(line, muted: true));
            }
        }
    }

    private TextBlock Line(string text, bool muted)
    {
        var block = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19 };
        block.Foreground = this.FindResource(muted ? "TextSecondaryBrush" : "TextPrimaryBrush") as IBrush;
        return block;
    }

    private void ShowState(string state)
    {
        AvailablePanel.IsVisible = state == "available";
        ProgressPanel.IsVisible = state == "progress";
        StartingPanel.IsVisible = state == "starting";
        NotesCard.Opacity = state == "available" ? 1 : 0.55;

        // As in the design, the version jump steps aside while the download runs.
        VersionRow.IsVisible = state != "progress";
    }

    private async void Install_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        if (!_vm.HasAppInstaller)
        {
            _vm.OpenReleasePage();
            Close();
            return;
        }

        ErrorText.IsVisible = false;
        ShowState("progress");
        DownloadBar.Value = 0;
        ProgressText.Text = string.Empty;
        _download = new CancellationTokenSource();

        var progress = new Progress<(long Read, long? Total)>(p => Dispatcher.UIThread.Post(() => ReportProgress(p.Read, p.Total)));
        var path = await _vm.DownloadAppInstallerAsync(progress, _download.Token);
        var cancelled = _download.IsCancellationRequested;
        _download = null;

        if (path is not null)
        {
            ShowState("starting");

            // A beat so the hand-over message is readable before the app closes.
            await Task.Delay(900);
            _vm.LaunchInstallerAndExit(path);
            return;
        }

        ShowState("available");
        if (!cancelled)
        {
            ErrorText.Text = "The download didn't finish. Check your connection and try again, or use View on GitHub to download it yourself.";
            ErrorText.IsVisible = true;
        }
    }

    private void ReportProgress(long read, long? total)
    {
        var readMb = read / 1048576.0;
        if (total is > 0)
        {
            var percent = read * 100.0 / total.Value;
            DownloadBar.IsIndeterminate = false;
            DownloadBar.Value = percent;
            ProgressText.Text = $"{readMb:0.0} / {total.Value / 1048576.0:0.0} MB · {percent:0}%";
        }
        else
        {
            DownloadBar.IsIndeterminate = true;
            ProgressText.Text = $"{readMb:0.0} MB";
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => _download?.Cancel();

    private void ViewOnGitHub_Click(object? sender, RoutedEventArgs e) => _vm?.OpenReleasePage();

    private void Skip_Click(object? sender, RoutedEventArgs e)
    {
        _vm?.SkipAppVersion();
        Close();
    }

    private void Later_Click(object? sender, RoutedEventArgs e) => Close();
}
