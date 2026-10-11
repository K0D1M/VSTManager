using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
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

    // ---- folder drag-and-drop ------------------------------------------------
    // In-process custom formats for carrying a view-model object (not a file) through Avalonia's
    // DataTransfer — confirmed via reflection against Avalonia.Base.dll that
    // DataFormat.CreateInProcessFormat<T> and DataTransferItem.Create<T>/TryGetValue<T> exist for
    // exactly this case; there was no existing example of it in this codebase (the only other
    // drag-drop here, PluginDetailWindow's image drop, is file-only via TryGetFiles()).
    private static readonly DataFormat<PluginCardViewModel> PluginDragFormat =
        DataFormat.CreateInProcessFormat<PluginCardViewModel>("application/x-vstmanager-plugin");

    private static readonly DataFormat<FolderNodeViewModel> FolderDragFormat =
        DataFormat.CreateInProcessFormat<FolderNodeViewModel>("application/x-vstmanager-folder");

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        _vm.NotificationRequested += (_, n) => _notifications.Show(n.Title, n.Message, n.ImagePath);

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

        // Window-level, not per-card: the drag-distance threshold has to keep tracking the
        // pointer after it leaves whatever card/folder row was originally pressed.
        AddHandler(PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel);

        // Seeded from the shared library.json, so a zoom set in the WPF head is the one this
        // opens at. Assigned before subscribing: the initial value is already persisted, and
        // saving it back here would be a pointless write on every launch.
        UiScale.Factor = _vm.SavedUiScale;
        ZoomHost.LayoutTransform = new ScaleTransform(UiScale.Factor, UiScale.Factor);
        UiScale.Changed += ApplyZoom;
        Closed += (_, _) => UiScale.Changed -= ApplyZoom;

        FitWidthToCards();

        // Tunnel, ahead of the ScrollViewer's own wheel handling, so a notch this handles never
        // also triggers the built-in (row-unaware) scroll underneath it.
        GridScroll.AddHandler(PointerWheelChangedEvent, OnLibraryWheel, RoutingStrategies.Tunnel);
        ListScroll.AddHandler(PointerWheelChangedEvent, OnLibraryWheel, RoutingStrategies.Tunnel);

        // ScrollChanged does not reliably fire for a manual Offset assignment (see
        // OnLibraryWheel, which sets scroll.Offset directly for the row-snap step) — confirmed by
        // tracing: it fired once at startup and never again during an actual scroll. Subscribing
        // to the OffsetProperty change directly is the same signal WPF's ScrollChanged is built
        // on and fires for every kind of scroll (wheel, scrollbar, or code), not just some.
        // PropertyChanged on OffsetProperty fires the instant the value is set, which can be
        // before layout has actually repositioned the headers TranslatePoint reads from — the
        // direct cause of a brief overlap (the bottom-edge check itself is correct, but was
        // reading a stale, pre-layout position for one frame). LayoutUpdated was tried as a fix
        // (it only runs once a layout pass has actually completed) but crashes outright:
        // UpdateStickyHeader changes StickyHeader's own Text/IsVisible, which itself triggers
        // another layout pass, re-firing LayoutUpdated — Avalonia detects that as an infinite
        // layout loop and throws. Posting to the dispatcher at Loaded priority instead runs after
        // the current layout pass finishes without being called FROM inside one, so it reads
        // settled positions without re-entering layout itself.
        GridScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.OffsetProperty)
            {
                Dispatcher.UIThread.Post(() => UpdateStickyHeader(GridScroll, GridSectionHeaders), DispatcherPriority.Loaded);
            }
        };
        ListScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.OffsetProperty)
            {
                Dispatcher.UIThread.Post(() => UpdateStickyHeader(ListScroll, ListSectionHeaders), DispatcherPriority.Loaded);
            }
        };

        // The view model stays dialog-free, so opening a window is the view's job.
        _vm.DetailRequested += (_, card) =>
            new PluginDetailWindow(_vm, card).ShowDialog(this);

        _vm.SettingsRequested += (_, settings) =>
            new SettingsWindow(settings).ShowDialog(this);

        _vm.NewPluginsNeedInfo += (_, plugins) =>
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

    // ---- startup width ------------------------------------------------------------------
    // The grid wraps fixed-width cards, so any other width leaves an empty strip at the right.
    // These mirror the XAML: GridScroll's horizontal Padding (20 + 20), the WrapPanel's
    // ItemSpacing, and the density styles' card widths.

    private const double GridPagePadding = 40;
    private const double CardGap = 14;
    private const double ScreenFraction = 0.8;

    private static double CardWidth(ViewDensity density) => density switch
    {
        ViewDensity.Compact => 196,
        ViewDensity.Large => 320,
        _ => 252
    };

    /// <summary>
    /// Opens at a width that holds a whole number of cards per row: as many as fit in 80% of the
    /// screen (3 to 6), so the right edge isn't left with a gap. Applies to the grid layout only;
    /// the list has no natural width to fit.
    /// </summary>
    private void FitWidthToCards()
    {
        if (!_vm.IsGrid || Screens.Primary is not { } screen)
        {
            return;
        }

        var zoom = UiScale.Factor;
        var card = CardWidth(_vm.Density);

        // The content is laid out at window width / zoom, so work in content units.
        var budget = screen.WorkingArea.Width / screen.Scaling * ScreenFraction / zoom;
        var perRow = (int)Math.Floor((budget - GridPagePadding + CardGap) / (card + CardGap));
        perRow = Math.Clamp(perRow, 3, 6);

        var content = perRow * card + (perRow - 1) * CardGap + GridPagePadding;
        Width = Math.Max(MinWidth, content * zoom);
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

        // The app's own self-update check, distinct from StartUpdateChecks above (which is the
        // per-plugin "newer version on KVR" checker) — matching WPF, which runs this once on
        // every launch. Fire-and-forget: a failed check here is not worth delaying anything for.
        await _vm.CheckForAppUpdateCommand.ExecuteAsync(null);
        if (_vm.ShouldOfferAppUpdateAtLaunch && IsVisible)
        {
            await UpdateAvailableWindow.ShowAsync(this, _vm, offerSkip: true);
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

        // Opens on release rather than press (like WPF), and only if this didn't turn into a
        // drag onto a folder.
        _clickedCard = card;
        ArmDrag(e, () => BuildPluginDragData(card));
    }

    private static DataTransfer BuildPluginDragData(PluginCardViewModel card)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(PluginDragFormat, card));
        return data;
    }

    private static DataTransfer BuildFolderDragData(FolderNodeViewModel node)
    {
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(FolderDragFormat, node));
        return data;
    }

    // ---- drag-start threshold -------------------------------------------------
    // DoDragDropAsync starts showing drag-cursor feedback (the "no drop target" cursor) the
    // instant it's called — found by actually clicking a card and seeing the cursor flip on a
    // plain click with no movement at all. WPF avoids this with a manual pixel-distance check
    // before ever calling DoDragDrop; Avalonia's DoDragDropAsync takes PointerPressedEventArgs
    // directly and has no equivalent built-in threshold of its own, so the same manual check is
    // ported here rather than assumed unnecessary.

    private const double DragThreshold = 4;
    private Point? _dragStartPoint;
    private Func<DataTransfer>? _dragDataFactory;
    private PointerPressedEventArgs? _dragPressArgs;
    private PluginCardViewModel? _clickedCard;

    private void ArmDrag(PointerPressedEventArgs e, Func<DataTransfer> dataFactory)
    {
        _dragStartPoint = e.GetPosition(this);
        _dragDataFactory = dataFactory;
        _dragPressArgs = e;
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartPoint is not { } start || _dragDataFactory is not { } factory || _dragPressArgs is not { } pressArgs)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            ClearArmedDrag();
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < DragThreshold && Math.Abs(current.Y - start.Y) < DragThreshold)
        {
            return;
        }

        ClearArmedDrag();
        _clickedCard = null;
        _ = RunDragAsync(pressArgs, factory());
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ClearArmedDrag();

        var card = _clickedCard;
        _clickedCard = null;
        if (card is not null && e.InitialPressMouseButton == MouseButton.Left)
        {
            _vm.OpenDetailsCommand.Execute(card);
        }
    }

    private void ClearArmedDrag()
    {
        _dragStartPoint = null;
        _dragDataFactory = null;
        _dragPressArgs = null;
    }

    private static async Task RunDragAsync(PointerPressedEventArgs pressArgs, DataTransfer data)
    {
        try
        {
            await DragDrop.DoDragDropAsync(pressArgs, data, DragDropEffects.Move);
        }
        catch (InvalidOperationException)
        {
            // The shell occasionally refuses to start a second drag while one is still
            // unwinding — same tolerance WPF's own DragSource_MouseMove has for the COM
            // equivalent of this failure.
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
        // Same selection-aware scope RefreshAllMetadataAsync itself uses — the dialog title
        // should say what's actually about to happen rather than always claiming "all".
        var title = _vm.HasSelection ? "Check Updates for Selected" : "Refresh All Metadata";

        if (!await ConfirmDialog.ShowAsync(this, title,
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

        // Folders can be dragged onto other folders to nest them, same as WPF. This runs
        // alongside the filter-toggle above rather than instead of it — a click that never turns
        // into an actual drag just resolves as the ordinary row click it already was.
        ArmDrag(e, () => BuildFolderDragData(node));
    }

    /// <summary>
    /// Both plugin-onto-folder filing and folder-onto-folder nesting land here — the payload type
    /// decides which, same convention WPF's own drop handlers use. A folder can never be dropped
    /// onto itself or a descendant (CanMoveFolder, mirroring MainViewModel.MoveFolder's own guard)
    /// — checked here for the live highlight and re-checked on Drop since a fast drag can outrun
    /// DragOver's last update.
    /// </summary>
    private void FolderRow_DragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: FolderNodeViewModel node })
        {
            return;
        }

        var allowed = IsFolderDropAllowed(node, e);
        e.DragEffects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        node.IsDropTarget = allowed;
        e.Handled = true;
    }

    private void FolderRow_DragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Control { DataContext: FolderNodeViewModel node })
        {
            node.IsDropTarget = false;
        }
    }

    private void FolderRow_Drop(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: FolderNodeViewModel node })
        {
            return;
        }

        node.IsDropTarget = false;
        e.Handled = true;

        var plugin = e.DataTransfer.TryGetValue(PluginDragFormat);
        if (plugin is not null)
        {
            _vm.MovePluginsToFolder(plugin, node.Id);
            return;
        }

        var dragged = e.DataTransfer.TryGetValue(FolderDragFormat);
        if (dragged is not null && !_vm.MoveFolder(dragged.Id, node.Id))
        {
            _ = ConfirmDialog.ShowMessageAsync(this, "Move Folder",
                $"\"{dragged.Name}\" can't be moved inside itself.");
        }
    }

    private bool IsFolderDropAllowed(FolderNodeViewModel node, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(PluginDragFormat))
        {
            return true;
        }

        var dragged = e.DataTransfer.TryGetValue(FolderDragFormat);
        return dragged is not null
               && !string.Equals(dragged.Id, node.Id, StringComparison.OrdinalIgnoreCase)
               && _vm.CanMoveFolder(dragged.Id, node.Id);
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
        var candidates = RealizedSections(isGrid ? GridSections : ListSections)
            .Where(s => s.Section.IsExpanded)
            .Select(s => s.Items);

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

    // ---- sticky section headers -----------------------------------------------
    // A RenderTransform-in-place approach (translating the real header down by the scroll offset)
    // was tried and confirmed NOT to work: Avalonia's ScrollContentPresenter clips a scrolled
    // child to its own unscrolled layout position regardless of RenderTransform, so a translated
    // header still gets clipped away — a hard limitation of the stock ScrollViewer, not a bug in
    // the transform math (confirmed by tracing the offset/position math, which was correct, while
    // nothing visually stuck). This overlay instead lives as a sibling of the ScrollViewer,
    // outside its clip region entirely, and is shown/relabelled to match whichever section is
    // "stuck" — the same "read live Bounds off real controls" technique MeasureRowHeight already
    // uses for the row-snap wheel step.

    private readonly record struct SectionHeaderInfo(
        ToggleButton Header, ItemsControl Items, CategorySectionViewModel Section);

    /// <summary>Sections are generated from a template now, so their header/items controls are
    /// found in each realized container rather than referenced by name.</summary>
    private static IEnumerable<SectionHeaderInfo> RealizedSections(ItemsControl host)
    {
        foreach (var container in host.GetRealizedContainers())
        {
            if (container.DataContext is not CategorySectionViewModel section)
            {
                continue;
            }

            var descendants = container.GetVisualDescendants().ToList();
            var header = descendants.OfType<ToggleButton>().FirstOrDefault(t => t.Classes.Contains("sectionHeaderReal"));
            var items = descendants.OfType<ItemsControl>().FirstOrDefault(i => i.Classes.Contains("sectionItems"));

            if (header is not null && items is not null)
            {
                yield return new SectionHeaderInfo(header, items, section);
            }
        }
    }

    private IReadOnlyList<SectionHeaderInfo> GridSectionHeaders => RealizedSections(GridSections).ToList();

    private IReadOnlyList<SectionHeaderInfo> ListSectionHeaders => RealizedSections(ListSections).ToList();

    private SectionHeaderInfo? _stuckSection;

    /// <summary>
    /// The section whose real header has scrolled above the viewport's top edge, but whose
    /// content (or a later section not yet reached) is still what's on screen, "owns" the sticky
    /// overlay — exactly one at a time, swapping the instant the next real header reaches the top,
    /// which is what gives the classic grouped-list stick/push-off effect rather than headers
    /// overlapping or a gap with no header showing.
    /// </summary>
    private void UpdateStickyHeader(ScrollViewer scroll, IReadOnlyList<SectionHeaderInfo> sections)
    {
        SectionHeaderInfo? stuck = null;

        foreach (var section in sections)
        {
            if (section.Header.Bounds.Height <= 0 || section.Section.Count == 0)
            {
                continue;
            }

            // The real header's BOTTOM edge, not its top — checking the top alone left a window
            // where the real header was still partially visible (its bottom half not yet
            // scrolled past 0) while the overlay had already appeared, showing both stacked on
            // top of each other. Only once the whole header has scrolled out does the sticky
            // overlay take over from it.
            var bottomInViewport = section.Header.TranslatePoint(
                new Point(0, section.Header.Bounds.Height), scroll)?.Y ?? double.MaxValue;

            if (bottomInViewport <= 0)
            {
                stuck = section;
            }
        }

        _stuckSection = stuck;

        if (stuck is not { } section2)
        {
            StickyHeader.IsVisible = false;
            return;
        }

        StickyHeaderIcon.Text = section2.Section.Icon;
        StickyHeaderText.Text = section2.Section.Title;
        StickyHeaderCount.Text = $"({section2.Section.Count})";
        // IsChecked mirrors the real header's own expanded state, so the overlay shows the
        // correct chevron/pressed look immediately rather than only after the user clicks it once.
        StickyHeader.IsChecked = section2.Section.IsExpanded;
        StickyHeader.IsVisible = true;
    }

    /// <summary>
    /// Forwards to whichever section is currently stuck under the overlay — there is no bound
    /// Command here (unlike the real per-section headers) because which section that is changes
    /// as the user scrolls, so it has to be resolved at click time from _stuckSection rather than
    /// fixed in the markup.
    /// </summary>
    private void StickyHeader_Click(object? sender, RoutedEventArgs e)
    {
        if (_stuckSection is { } section)
        {
            // ToggleButton already flips its own IsChecked before Click fires; overwritten here
            // with the actual new view-model state (rather than trusting that automatic flip) so
            // the two can never end up disagreeing.
            var next = !section.Section.IsExpanded;
            section.Section.IsExpanded = next;
            StickyHeader.IsChecked = next;
        }
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
