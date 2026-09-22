using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace VstManager.Ui.Views;

/// <summary>
/// Stand-in for LoadingIndicators.Avalonia, which is built against Avalonia 11 and fails to load
/// on 12. Rotated by a frame timer rather than a XAML style animation, and the timer only runs
/// while the spinner is active and attached, so an idle spinner costs nothing.
/// </summary>
public partial class LoadingSpinner : UserControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<LoadingSpinner, bool>(nameof(IsActive), defaultValue: true);

    private readonly RotateTransform _outer = new();
    private readonly RotateTransform _inner = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _startedAt;
    private bool _attached;

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public LoadingSpinner()
    {
        InitializeComponent();
        Outer.RenderTransform = _outer;
        Inner.RenderTransform = _inner;
        _timer.Tick += (_, _) => Advance();
    }

    private void Advance()
    {
        var seconds = (DateTime.UtcNow - _startedAt).TotalSeconds;
        _outer.Angle = seconds * 330 % 360;
        _inner.Angle = 360 - seconds * 450 % 360;    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateRunning();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        UpdateRunning();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty)
        {
            UpdateRunning();
        }
    }

    private void UpdateRunning()
    {
        var run = IsActive && _attached;
        Root.IsVisible = IsActive;

        if (run && !_timer.IsEnabled)
        {
            _startedAt = DateTime.UtcNow;
            _timer.Start();
        }
        else if (!run && _timer.IsEnabled)
        {
            _timer.Stop();
        }
    }
}
