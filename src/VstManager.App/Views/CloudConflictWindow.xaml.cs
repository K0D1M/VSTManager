using System.Windows;
using VstManager.App.Controls;
using VstManager.App.Services;
using VstManager.Core.Services.Cloud;

namespace VstManager.App.Views;

/// <summary>
/// Asked when local and remote both changed since the last sync. Deliberately modal and
/// deliberately without a default: whichever side is discarded is gone, so the choice is the
/// user's rather than a timestamp heuristic's.
/// </summary>
public partial class CloudConflictWindow : Window
{
    public ConflictResolution Resolution { get; private set; } = ConflictResolution.Skip;

    public CloudConflictWindow(DateTime localChangedAt, DateTime remoteChangedAt)
    {
        InitializeComponent();
        MaximizedBoundsFix.Apply(this);
        WindowIcon.ApplyDefault(this);

        LocalTimestampText.Text = Describe(localChangedAt);
        RemoteTimestampText.Text = Describe(remoteChangedAt);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) => WindowSizing.FitToScreen(this);

    /// <summary>
    /// Both timestamps arrive as UTC; they're shown in local time because that's the only form
    /// a user can compare against their own memory of when they last changed something.
    /// </summary>
    private static string Describe(DateTime utcTimestamp)
    {
        var local = utcTimestamp.ToLocalTime();
        var age = DateTime.Now - local;

        var relative = age switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)age.TotalMinutes} minute(s) ago",
            { TotalHours: < 24 } => $"{(int)age.TotalHours} hour(s) ago",
            _ => $"{(int)age.TotalDays} day(s) ago"
        };

        return $"Last changed {local:dddd d MMMM, HH:mm} ({relative})";
    }

    private void KeepLocal_Click(object sender, RoutedEventArgs e) => Close(ConflictResolution.KeepLocal);

    private void KeepRemote_Click(object sender, RoutedEventArgs e) => Close(ConflictResolution.KeepRemote);

    private void DecideLater_Click(object sender, RoutedEventArgs e) => Close(ConflictResolution.Skip);

    private void Close(ConflictResolution resolution)
    {
        Resolution = resolution;
        DialogResult = true;
    }
}
