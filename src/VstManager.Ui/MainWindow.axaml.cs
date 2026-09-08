using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VstManager.Core.Services.Cloud;
using VstManager.Ui.Services;
using VstManager.Ui.ViewModels;
using VstManager.Ui.Views;

namespace VstManager.Ui;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private readonly NotificationService _notifications = new();
    private readonly TrayIconService _tray = new();

    /// <summary>Set by the tray's Exit so the close handler stops intercepting and really closes.</summary>
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.NotificationRequested += (_, n) => _notifications.Show(n.Title, n.Message);

        _tray.OpenRequested += (_, _) => RestoreFromTray();
        _tray.ExitRequested += (_, _) =>
        {
            _isExiting = true;
            Close();
        };

        Closing += OnMainWindowClosing;

        // The view model stays dialog-free, so opening a window is the view's job.
        _vm.DetailRequested += (_, card) =>
            new PluginDetailWindow(_vm, card).ShowDialog(this);

        _vm.SettingsRequested += (_, settings) =>
            new SettingsWindow(settings).ShowDialog(this);

        _vm.NewMultiCopyPluginsFound += (_, plugins) =>
            new ClassifyPluginsWindow(_vm, plugins).ShowDialog(this);

        // The sync runs on a background thread and blocks on this answer, so the dialog is shown
        // here and its result handed back through the request's completion source.
        _vm.CloudConflictRequested += async (_, request) =>
        {
            try
            {
                var choice = await CloudConflictWindow.AskAsync(this, request.LocalChangedAt, request.RemoteChangedAt);
                request.Completion.TrySetResult(choice);
            }
            catch (Exception)
            {
                // Never leave the sync awaiting forever; changing nothing is the safe answer.
                request.Completion.TrySetResult(ConflictResolution.Skip);
            }
        };
    }

    /// <summary>
    /// With "Minimize to tray" on, closing hides the window instead of exiting — the app keeps
    /// scanning in the background and the tray menu is the way back. Real closing happens only
    /// through the tray's Exit, which sets _isExiting first.
    /// </summary>
    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        // IsAvailable guards against hiding the window with no way to restore it: if the tray
        // icon could not be created, closing must still close.
        if (!_isExiting && _vm.MinimizeToTray && _tray.IsAvailable)
        {
            e.Cancel = true;
            Hide();
            _tray.Show();
            return;
        }

        // The lifetime runs in OnExplicitShutdown (see App), so that hiding to the tray is not
        // mistaken for quitting. The cost is that a real close has to end the app itself.
        _tray.Dispose();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void RestoreFromTray()
    {
        _tray.Hide();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Attached here, not in the constructor: WindowNotificationManager is a TemplatedControl
        // that adopts itself into the window's visual tree, which does not exist until the
        // template has been applied. Constructed too early it silently shows nothing.
        _notifications.Attach(Notifications);

        // Scan after the first paint, so the window appears immediately with its loading state
        // rather than hanging on a blank frame while the filesystem is walked.
        await _vm.LoadAsync();
    }

    /// <summary>
    /// The sort ComboBox carries its SortOption name in each item's Tag. Handled here rather than
    /// bound because SetSort doubles as the direction toggle, which a SelectedItem binding would
    /// trigger spuriously.
    /// </summary>
    private void OnSortChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: ComboBoxItem { Tag: string sort } }
            && Enum.TryParse<SortOption>(sort, out var parsed)
            && parsed != _vm.Sort)
        {
            _vm.Sort = parsed;
        }
    }

    // ---- selection ---------------------------------------------------------
    // The library is an ItemsControl, which has no selection model of its own — unlike ListBox,
    // which brings one but would fight the card/row templates and the wrapping grid panel. So
    // selection lives on the cards themselves and is driven from here.

    /// <summary>The last card clicked without Shift, anchoring the next range selection.</summary>
    private PluginCardViewModel? _rangeAnchor;

    private void OnPluginPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: PluginCardViewModel card })
        {
            return;
        }

        // Keep focus on the card so the arrow keys have somewhere to move from, and so the
        // focus ring shows which item the keyboard is acting on.
        (sender as Control)?.Focus();

        var point = e.GetCurrentPoint(sender as Control);

        // A right-click opens the flyout; it must not collapse a multi-selection the user just
        // built, or "apply to all selected" would silently become "apply to this one".
        if (point.Properties.IsRightButtonPressed)
        {
            if (!card.IsSelected)
            {
                _vm.SelectOnly(card);
                _rangeAnchor = card;
            }

            return;
        }

        var modifiers = e.KeyModifiers;

        if (modifiers.HasFlag(KeyModifiers.Shift) && _rangeAnchor is not null)
        {
            _vm.SelectRange(_rangeAnchor, card);
            return;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            card.IsSelected = !card.IsSelected;
            _rangeAnchor = card;
            return;
        }

        // A plain click on an already-selected card would normally start a drag in a file
        // manager; with nothing to drag onto yet, collapsing to just that card is the
        // least surprising behaviour.
        _vm.SelectOnly(card);
        _rangeAnchor = card;
    }

    private void OnPluginDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: PluginCardViewModel card })
        {
            _vm.OpenDetailsCommand.Execute(card);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Library-wide shortcuts. Bound at the window rather than per-card so they work whether or
    /// not a card currently holds focus — except the per-item ones, which need a focused card.
    /// </summary>
    private void OnLibraryKeyDown(object? sender, KeyEventArgs e)
    {
        // Never steal keys from the search box or any other text entry.
        if (e.Source is TextBox)
        {
            return;
        }

        var focused = (FocusManager?.GetFocusedElement() as Control)?.DataContext as PluginCardViewModel;

        switch (e.Key)
        {
            case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _vm.SelectAllCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                _vm.ClearSelectionCommand.Execute(null);
                _rangeAnchor = null;
                e.Handled = true;
                break;

            case Key.Enter when focused is not null:
                _vm.OpenDetailsCommand.Execute(focused);
                e.Handled = true;
                break;

            case Key.Space when focused is not null:
                _vm.ToggleFavoriteCommand.Execute(focused);
                e.Handled = true;
                break;

            case Key.Delete when focused is not null:
                _vm.ToggleHiddenCommand.Execute(focused);
                e.Handled = true;
                break;
        }
    }
}
