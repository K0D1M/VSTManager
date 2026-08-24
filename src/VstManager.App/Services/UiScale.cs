using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VstManager.App.Services;

/// <summary>
/// The app-wide content zoom factor (1.0 = 100%), and the attached property that applies it to a
/// window's content.
///
/// Held as a global static rather than on a view model because the zoom must reach every window,
/// and several of them (Welcome, Classify, MatchPicker, CloudConflict) don't share MainViewModel
/// as their DataContext. MainViewModel owns only persistence and the Settings UI; this class is
/// the single source of truth the windows actually read.
/// </summary>
public static class UiScale
{
    public const double Min = 0.8;
    public const double Max = 1.5;
    public const double Step = 0.1;

    private static double _factor = 1.0;

    /// <summary>Raised whenever <see cref="Factor"/> changes, so open windows can re-scale live.</summary>
    public static event Action? Changed;

    /// <summary>The current zoom, clamped to [<see cref="Min"/>, <see cref="Max"/>].</summary>
    public static double Factor
    {
        get => _factor;
        set
        {
            var clamped = Math.Clamp(Math.Round(value, 2), Min, Max);
            if (Math.Abs(clamped - _factor) < 0.001)
            {
                return;
            }

            _factor = clamped;
            Changed?.Invoke();
        }
    }

    /// <summary>Nudges the zoom by one step, for the Ctrl+/Ctrl- shortcuts.</summary>
    public static void StepBy(double steps) => Factor += steps * Step;

    public static void Reset() => Factor = 1.0;

    /// <summary>
    /// Attach to the element holding a window's content (the row below the title bar). When true,
    /// that element gets a LayoutTransform ScaleTransform bound to <see cref="Factor"/>, kept in
    /// sync via <see cref="Changed"/>.
    ///
    /// LayoutTransform, deliberately, not RenderTransform: it re-runs layout at the scaled size,
    /// so WrapPanel reflow, ScrollViewers and star-sizing all keep working — bigger content that
    /// still reflows, not a stretched bitmap.
    /// </summary>
    public static readonly DependencyProperty ScaleContentProperty =
        DependencyProperty.RegisterAttached(
            "ScaleContent",
            typeof(bool),
            typeof(UiScale),
            new PropertyMetadata(false, OnScaleContentChanged));

    public static void SetScaleContent(DependencyObject element, bool value) =>
        element.SetValue(ScaleContentProperty, value);

    public static bool GetScaleContent(DependencyObject element) =>
        (bool)element.GetValue(ScaleContentProperty);

    private static void OnScaleContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
        {
            return;
        }

        var transform = new ScaleTransform(_factor, _factor);
        element.LayoutTransform = transform;

        void Apply()
        {
            transform.ScaleX = transform.ScaleY = _factor;
            ApplyTextMode(element);
        }

        Apply();
        Changed += Apply;

        // Drop the subscription when the element leaves the tree, so closed windows don't leak or
        // keep firing. Unloaded can fire more than once (e.g. reparenting), so it's idempotent.
        element.Unloaded += (_, _) => Changed -= Apply;
    }

    /// <summary>
    /// Picks the text rendering mode that stays crisp for the current zoom.
    ///
    /// At exactly 100% the default "Display" formatting snaps glyphs to whole device pixels and is
    /// sharpest. But under a scale, those pixel-snapped glyphs get resampled and go soft — bold
    /// weights worst, since their heavier stems show the blur most. "Ideal" positions glyphs in
    /// continuous coordinates, so they scale cleanly. Switch to it only while zoomed, so 100% keeps
    /// the crisper Display path.
    /// </summary>
    private static void ApplyTextMode(FrameworkElement element)
    {
        var zoomed = Math.Abs(_factor - 1.0) > 0.001;

        TextOptions.SetTextFormattingMode(
            element,
            zoomed ? TextFormattingMode.Ideal : TextFormattingMode.Display);

        // Auto lets WPF choose ClearType/greyscale per the mode; explicit so it isn't left on a
        // stale value after toggling formatting mode.
        TextOptions.SetTextRenderingMode(element, TextRenderingMode.Auto);
    }
}
