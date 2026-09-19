using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using VstManager.Core.Models;
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

    /// <summary>
    /// Set once a real close has started. Without this, calling desktop.Shutdown() from inside
    /// Closing recurses forever: Shutdown() closes every open window, which closes this one
    /// again, which fires Closing again, which calls Shutdown() again — a stack overflow on
    /// every normal close with tray-minimize off (the default), found by actually closing the
    /// window rather than always killing the process by PID during testing.
    /// </summary>
    private bool _isShuttingDown;

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

        // Ctrl+wheel is handled while the event tunnels down, before the ScrollViewer sees it, so
        // zooming does not also scroll the library out from under the cursor.
        AddHandler(PointerWheelChangedEvent, OnPreviewWheel, RoutingStrategies.Tunnel);

        // Seeded from the shared library.json, so a zoom set in the WPF head is the one this
        // opens at. Assigned before subscribing: the initial value is already persisted, and
        // saving it back here would be a pointless write on every launch.
        UiScale.Factor = _vm.SavedUiScale;
        ZoomHost.LayoutTransform = new ScaleTransform(UiScale.Factor, UiScale.Factor);
        UiScale.Changed += ApplyZoom;
        Closed += (_, _) => UiScale.Changed -= ApplyZoom;

        // Tunnel, ahead of the ScrollViewer's own wheel handling, so a notch this handles never
        // also triggers the built-in (row-unaware) scroll underneath it.
        GridScroll.AddHandler(PointerWheelChangedEvent, OnLibraryWheel, RoutingStrategies.Tunnel);
        ListScroll.AddHandler(PointerWheelChangedEvent, OnLibraryWheel, RoutingStrategies.Tunnel);

        // The view model stays dialog-free, so opening a window is the view's job.
        _vm.DetailRequested += (_, card) =>
            new PluginDetailWindow(_vm, card).ShowDialog(this);

        _vm.SettingsRequested += (_, settings) =>
            new SettingsWindow(settings).ShowDialog(this);

        _vm.NewMultiCopyPluginsFound += (_, plugins) =>
            new ClassifyPluginsWindow(_vm, plugins).ShowDialog(this);

        // Opens right after CreateFolder, so personalising a new folder is one continuous action
        // rather than a second trip through the context menu.
        _vm.FolderEditRequested += async (_, folder) => await EditFolderAsync(folder);

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
        // Re-entrant call from inside desktop.Shutdown() below, already tearing down — let this
        // second pass through Closing proceed (Cancel stays false) instead of calling Shutdown()
        // again.
        if (_isShuttingDown)
        {
            return;
        }

        // IsAvailable guards against hiding the window with no way to restore it: if the tray
        // icon could not be created, closing must still close.
        if (!_isExiting && _vm.MinimizeToTray && _tray.IsAvailable)
        {
            e.Cancel = true;
            Hide();
            _tray.Show();
            return;
        }

        _isShuttingDown = true;
        _vm.StopUpdateChecks();

        // The lifetime runs in OnExplicitShutdown (see App), so that hiding to the tray is not
        // mistaken for quitting. The cost is that a real close has to end the app itself.
        _tray.Dispose();
        _notifications.Dispose();

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

    /// <summary>
    /// Quits immediately regardless of "Minimize to tray" — same _isExiting bypass the tray's own
    /// Exit uses (see OnMainWindowClosing), so a user who wants out doesn't have to know that
    /// setting exists or dig it out of the tray icon to actually close the app.
    /// </summary>
    private void Quit_Click(object? sender, RoutedEventArgs e)
    {
        _isExiting = true;
        Close();
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Native notification setup can touch the registry/filesystem (Windows) or request OS
        // permission (macOS), so it happens after the first paint alongside the scan below,
        // rather than blocking the window from appearing at all.
        _ = _notifications.InitializeAsync();

        // Scan after the first paint, so the window appears immediately with its loading state
        // rather than hanging on a blank frame while the filesystem is walked.
        await _vm.LoadAsync();
        _vm.StartUpdateChecks();
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

    // ---- folders ------------------------------------------------------------

    /// <summary>
    /// Opens the folder picker for the plugin this menu item's flyout was opened on. A plain
    /// Click handler rather than a bound Command: it already has the card straight off this
    /// item's own DataContext, with no combined-parameter plumbing needed.
    /// </summary>
    private async void MoveToFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PluginCardViewModel card })
        {
            return;
        }

        var result = await FolderPickerWindow.PickAsync(this, _vm.FolderMenuEntries);

        if (result is not null)
        {
            _vm.MovePluginsToFolder(card, result.FolderId);
        }
    }

    // ---- uninstall -----------------------------------------------------------

    /// <summary>
    /// Looks up whether each target has a registered uninstaller so the confirmation can name the
    /// right action, confirms once — twice for a single plugin, matching how deleting one
    /// irreversible thing gets more friction than deleting many at once elsewhere in this app —
    /// then runs every plan and reports any failures together.
    /// </summary>
    private async void Uninstall_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: PluginCardViewModel card })
        {
            return;
        }

        var plans = _vm.PrepareUninstall(card);
        if (plans.Count == 0)
        {
            return;
        }

        if (plans.Count == 1)
        {
            var plan = plans[0];

            var firstMessage = plan.Uninstaller is not null
                ? $"Run the uninstaller for \"{plan.Uninstaller.DisplayName}\"?"
                : $"No registered uninstaller was found for \"{plan.Card.Name}\".\n\n"
                  + $"Delete the plugin file(s) directly instead?\n\n{plan.Card.ActiveInstalls.FirstOrDefault()?.Path}";

            if (!await ConfirmDialog.ShowAsync(this, "Uninstall Plugin", firstMessage, "Continue"))
            {
                return;
            }

            if (!await ConfirmDialog.ShowAsync(this, "Confirm Uninstall",
                    $"This will permanently uninstall \"{plan.Card.Name}\" from this computer.\n\n"
                    + "This action cannot be undone. Are you sure you want to continue?", "Uninstall"))
            {
                return;
            }
        }
        else
        {
            var names = string.Join("\n", plans.Select(p => p.Card.Name));

            if (!await ConfirmDialog.ShowAsync(this, "Uninstall Plugins",
                    $"Uninstall these {plans.Count} plugins?\n\n{names}\n\n"
                    + "Plugins with a registered uninstaller will open their installer window one at a "
                    + "time — close each as it appears to continue to the next. This cannot be undone.",
                    "Uninstall"))
            {
                return;
            }
        }

        var failures = new List<string>();

        foreach (var plan in plans)
        {
            var failure = await _vm.ExecuteUninstallAsync(plan);

            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        if (failures.Count > 0)
        {
            await ConfirmDialog.ShowMessageAsync(this, "Uninstall Failed",
                "Some plugins couldn't be fully uninstalled:\n\n" + string.Join("\n", failures));
        }

        await _vm.RescanCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// The toolbar's "Refresh All Metadata" — re-detects installed versions and re-queries KVR
    /// for every installed plugin, matching the WPF app's confirm-then-run flow. The confirmation
    /// lives here rather than in the view model, this project's established pattern for anything
    /// destructive-adjacent (see Uninstall_Click above).
    /// </summary>
    private async void RefreshAllMetadata_Click(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDialog.ShowAsync(this, "Refresh All Metadata",
                "This action will take some time. Continue?", "Continue"))
        {
            return;
        }

        await _vm.RefreshAllMetadataAsync();
    }

    /// <summary>
    /// Clicking a folder row narrows the main grid to it — this layout has no sidebar to host a
    /// separate per-folder view, so the folder acts as one more single-choice filter. Clicking
    /// the already-selected folder clears it, so there is always a one-click way back to "all".
    /// </summary>
    private void OnFolderRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: FolderNodeViewModel node }
            || e.GetCurrentPoint(sender as Control).Properties.IsRightButtonPressed)
        {
            // A right-click only opens the context menu; it must not also toggle the filter.
            return;
        }

        _vm.SelectedFolderId = _vm.SelectedFolderId == node.Id ? null : node.Id;
    }

    private async void EditFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FolderNodeViewModel node })
        {
            await EditFolderAsync(node.Folder);
        }
    }

    private async Task EditFolderAsync(PluginFolder folder)
    {
        var result = await FolderEditWindow.EditAsync(this, folder);

        if (result is { } edited)
        {
            _vm.ApplyFolderEdit(folder, edited.Name, edited.Icon, edited.ColorHex);
        }
    }

    private async void DeleteFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: FolderNodeViewModel node })
        {
            return;
        }

        var (subfolderCount, affected) = _vm.DescribeFolderDeletion(node.Folder);

        var message = subfolderCount > 0
            ? $"Delete \"{node.Name}\" and its {subfolderCount} sub-folder(s)?"
            : $"Delete \"{node.Name}\"?";

        if (affected > 0)
        {
            message += $"\n\n{affected} plugin(s) will move back to Unfiled. The plugins themselves aren't touched.";
        }

        if (await ConfirmDialog.ShowAsync(this, "Delete Folder", message, "Delete"))
        {
            _vm.DeleteFolderCommand.Execute(node.Folder);
        }
    }

    /// <summary>
    /// Library-wide shortcuts. Bound at the window rather than per-card so they work whether or
    /// not a card currently holds focus — except the per-item ones, which need a focused card.
    /// </summary>
    private void OnLibraryKeyDown(object? sender, KeyEventArgs e)
    {
        // Zoom is checked before the TextBox guard below: Ctrl +/-/0 should work while the caret
        // is in the search box, exactly as it does in a browser.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && TryHandleZoomKey(e.Key))
        {
            e.Handled = true;
            return;
        }

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

    // ---- zoom --------------------------------------------------------------
    // Ctrl +/-/0 and Ctrl+wheel, the familiar browser binding. The factor itself lives in the
    // global UiScale service so every window can read it; this only drives it and reflects it.

    /// <summary>
    /// Handles the zoom keys. OemPlus/OemMinus are the main row; Add/Subtract/NumPad0 cover the
    /// numpad. Returns false for anything else so other Ctrl shortcuts still work.
    /// </summary>
    private bool TryHandleZoomKey(Key key)
    {
        switch (key)
        {
            case Key.OemPlus or Key.Add:
                UiScale.StepBy(1);
                break;

            case Key.OemMinus or Key.Subtract:
                UiScale.StepBy(-1);
                break;

            case Key.D0 or Key.NumPad0:
                UiScale.Reset();
                break;

            default:
                return false;
        }

        ShowZoomIndicator();
        return true;
    }

    /// <summary>
    /// Ctrl + wheel zooms. Handled in the tunnelling phase so it both zooms and stops the library
    /// scrolling at the same time — otherwise the list moves under the cursor while zooming.
    /// </summary>
    private void OnPreviewWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0)
        {
            return;
        }

        UiScale.StepBy(e.Delta.Y > 0 ? 1 : -1);
        ShowZoomIndicator();
        e.Handled = true;
    }

    /// <summary>Pushes the current factor onto the transform and persists it.</summary>
    private void ApplyZoom()
    {
        ZoomHost.LayoutTransform = new ScaleTransform(UiScale.Factor, UiScale.Factor);
        _vm.SaveUiScale(UiScale.Factor);

        // Nothing to re-measure here: RowHeight below reads live Bounds off the realized
        // containers on every wheel notch, and LayoutTransformControl has already re-run layout
        // by the time this runs, so the very next wheel event sees the new, correctly-scaled size.
    }

    /// <summary>
    /// One wheel notch scrolls the library by exactly one visual row, rather than Avalonia's
    /// built-in default of a fixed number of arbitrary "lines" — SmallChange/LargeChange on
    /// ScrollViewer are read-only and computed internally, so there is no supported way to hand
    /// it a row height; the wheel is handled directly instead and Offset is driven by hand.
    ///
    /// Runs on GridScroll and ListScroll's own PointerWheelChanged, ahead of the ScrollViewer's
    /// own handling (Tunnel), so the built-in scroll never fires for a notch this has handled.
    /// </summary>
    private void OnLibraryWheel(object? sender, PointerWheelEventArgs e)
    {
        // Ctrl+wheel is zoom, not scroll — see OnPreviewWheel, which runs first (registered at
        // the Window level) and already marks the event handled for that case.
        if (e.Handled || e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Delta.Y == 0)
        {
            return;
        }

        if (sender is not ScrollViewer scroll)
        {
            return;
        }

        var isGrid = ReferenceEquals(scroll, GridScroll);

        // Sections are independently collapsible, so whichever one the user is actually looking
        // at (or the first with anything realized) determines the row height — there is no
        // single "the" list to measure any more now that the library is grouped by Kind.
        var candidates = isGrid
            ? new ItemsControl[] { InstrumentGridItems, EffectGridItems, UnclassifiedGridItems }
            : new ItemsControl[] { InstrumentListItems, EffectListItems, UnclassifiedListItems };

        double? rowHeight = null;
        foreach (var candidate in candidates)
        {
            rowHeight = MeasureRowHeight(candidate, isGrid);
            if (rowHeight is not null)
            {
                break;
            }
        }

        if (rowHeight is not { } step)
        {
            // Nothing realized yet to measure (e.g. an empty library) — let the default handling
            // run rather than silently doing nothing.
            return;
        }

        var current = scroll.Offset;
        var max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        var target = Math.Clamp(current.Y - Math.Sign(e.Delta.Y) * step, 0, max);

        scroll.Offset = current.WithY(target);
        e.Handled = true;
    }

    /// <summary>
    /// The height of one visual row: a realized container's own rendered height, plus — for the
    /// wrapping grid view, where several cards share a line — the WrapPanel's LineSpacing (14,
    /// matching MainWindow.axaml) to reach the next line's top rather than just the card's own
    /// bottom edge.
    ///
    /// Measured live off Bounds rather than a stored/hardcoded constant: Bounds already reflects
    /// whatever LayoutTransformControl has scaled the content to, so this is correct at any zoom
    /// level with no zoom arithmetic duplicated here.
    /// </summary>
    private static double? MeasureRowHeight(ItemsControl items, bool isGrid)
    {
        var container = items.GetRealizedContainers().FirstOrDefault();
        if (container is null || container.Bounds.Height <= 0)
        {
            return null;
        }

        return container.Bounds.Height + (isGrid ? 14 : 0);
    }

    private CancellationTokenSource? _zoomIndicatorCts;

    /// <summary>
    /// Flashes the current percentage, then fades it. Reads the value back from the service so it
    /// shows the clamped number — that is the only cue the user has hit the 80%/150% end.
    /// </summary>
    private async void ShowZoomIndicator()
    {
        ZoomIndicatorText.Text = $"{UiScale.Percent}%";
        ZoomIndicator.IsVisible = true;
        ZoomIndicator.Opacity = 1;

        // Restart the countdown on every change, so holding the shortcut keeps it on screen
        // instead of flickering.
        _zoomIndicatorCts?.Cancel();
        _zoomIndicatorCts = new CancellationTokenSource();
        var token = _zoomIndicatorCts.Token;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), token);
            ZoomIndicator.Opacity = 0;

            // Let the fade transition finish before it stops being hit-testable/visible.
            await Task.Delay(TimeSpan.FromMilliseconds(300), token);
            ZoomIndicator.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change, which owns the indicator now.
        }
    }
}
