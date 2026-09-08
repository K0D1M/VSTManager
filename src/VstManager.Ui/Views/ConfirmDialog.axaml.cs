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
    public static Task<bool> ShowAsync(Window owner, string title, string message, string yesLabel = "Yes")
    {
        var dialog = new ConfirmDialog { Title = title };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.YesButton.Content = yesLabel;
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

    private void Yes_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void No_Click(object? sender, RoutedEventArgs e) => Close(false);
}
