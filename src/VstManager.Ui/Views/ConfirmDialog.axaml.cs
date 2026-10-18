using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VstManager.Ui.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>Yes/No question. Resolves false when dismissed with the window's close button.</summary>
    public static Task<bool> ShowAsync(Window owner, string title, string message, string yesLabel = "Yes",
                                       string noLabel = "Cancel")
    {
        var dialog = new ConfirmDialog { Title = title };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.YesButton.Content = yesLabel;
        dialog.NoButton.Content = noLabel;
        return dialog.ShowDialog<bool>(owner);
    }

    /// <summary>Plain message with a single OK.</summary>
    public static Task ShowMessageAsync(Window owner, string title, string message)
    {
        var dialog = new ConfirmDialog { Title = title };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.NoButton.IsVisible = false;
        dialog.YesButton.Content = "OK";
        return dialog.ShowDialog<bool>(owner);
    }

    /// <summary>
    /// Same themed dialog as <see cref="ShowMessageAsync"/>, for the one startup case with no
    /// owner window yet to center over — the single-instance check, which has to run before
    /// MainWindow (or Welcome) is ever created. ShowDialog requires a non-null owner in Avalonia,
    /// so this shows modelessly instead and centers on screen rather than over an owner.
    /// </summary>
    public static Task ShowStandaloneMessageAsync(string title, string message)
    {
        var tcs = new TaskCompletionSource();
        var dialog = new ConfirmDialog
        {
            Title = title,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.NoButton.IsVisible = false;
        dialog.YesButton.Content = "OK";
        dialog.Closed += (_, _) => tcs.TrySetResult();
        dialog.Show();
        return tcs.Task;
    }

    private void Yes_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void No_Click(object? sender, RoutedEventArgs e) => Close(false);
}
