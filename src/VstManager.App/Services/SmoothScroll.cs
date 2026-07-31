using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace VstManager.App.Services;

/// <summary>
/// Gives every ScrollViewer animated, inertia-free smooth wheel scrolling instead of WPF's
/// default behaviour, which jumps in discrete 3-line steps and feels dated next to modern apps.
///
/// Attached globally from App startup via <see cref="EnableGlobally"/>, so it also applies to
/// scroll areas nested inside control templates without touching each XAML file.
/// </summary>
public static class SmoothScroll
{
    /// <summary>Pixels moved per wheel notch. Roughly matches a browser's step.</summary>
    private const double StepSize = 110;

    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(260);

    /// <summary>
    /// The scroll offset each ScrollViewer is animating *towards*. Tracked separately because
    /// reading VerticalOffset mid-animation gives the current interpolated position, so rapid
    /// wheel notches would each restart from wherever the last one happened to be — making
    /// fast scrolling lag badly behind the wheel.
    /// </summary>
    private static readonly DependencyProperty TargetOffsetProperty =
        DependencyProperty.RegisterAttached("TargetOffset", typeof(double), typeof(SmoothScroll),
            new PropertyMetadata(double.NaN));

    /// <summary>Drives the actual scroll position; ScrollViewer.VerticalOffset is read-only so it can't be animated directly.</summary>
    private static readonly DependencyProperty AnimatedOffsetProperty =
        DependencyProperty.RegisterAttached("AnimatedOffset", typeof(double), typeof(SmoothScroll),
            new PropertyMetadata(0.0, OnAnimatedOffsetChanged));

    private static void OnAnimatedOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer viewer && e.NewValue is double offset)
        {
            viewer.ScrollToVerticalOffset(offset);
        }
    }

    public static void EnableGlobally()
    {
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnPreviewMouseWheel));
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.Handled || e.Delta == 0)
        {
            return;
        }

        // Nothing to scroll vertically: leave the event alone so it bubbles to an outer
        // ScrollViewer that can handle it (e.g. a list inside a scrolling settings page).
        if (viewer.ScrollableHeight <= 0 || viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
        {
            return;
        }

        var current = (double)viewer.GetValue(TargetOffsetProperty);
        if (double.IsNaN(current))
        {
            current = viewer.VerticalOffset;
        }

        var target = Math.Clamp(current - Math.Sign(e.Delta) * StepSize, 0, viewer.ScrollableHeight);

        // Already pinned at the edge in the direction of travel — don't swallow the event, so
        // a parent scroll area can take over instead of the wheel appearing to do nothing.
        if (Math.Abs(target - current) < 0.5
            && (target <= 0 || target >= viewer.ScrollableHeight))
        {
            return;
        }

        viewer.SetValue(TargetOffsetProperty, target);

        var animation = new DoubleAnimation
        {
            From = viewer.VerticalOffset,
            To = target,
            Duration = Duration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        // Land exactly on the target and drop the animation, so the value doesn't snap back
        // when the storyboard is released.
        animation.Completed += (_, _) =>
        {
            viewer.BeginAnimation(AnimatedOffsetProperty, null);
            viewer.SetValue(AnimatedOffsetProperty, target);
        };

        viewer.BeginAnimation(AnimatedOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
        e.Handled = true;
    }
}
