using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;

namespace VstManager.Ui.Services;

/// <summary>
/// The app's code-driven animations: reveals (fade + small slide), the favourite pop, and the
/// window entrance. Hover lifts, sliding pills and width changes are XAML transitions instead.
/// With <see cref="Reduced"/> on, everything here drops its movement and keeps only the fade.
/// </summary>
public static class Motion
{
    /// <summary>Settings → Appearance → Reduce motion. Read on every call, so it applies at once.</summary>
    public static bool Reduced { get; set; }

    private static readonly Easing EaseOut = new CubicEaseOut();

    /// <summary>
    /// Fades <paramref name="target"/> in from a small offset. FillMode.Backward holds the start
    /// state through the delay, so staggered items stay hidden until their turn instead of
    /// flashing in and then animating.
    /// </summary>
    public static Task Reveal(Visual target, double dx = 0, double dy = 12, double fromScale = 1,
                              int durationMs = 300, int delayMs = 0, CancellationToken token = default)
    {
        var from = new Setter(Visual.OpacityProperty, 0d);
        var to = new Setter(Visual.OpacityProperty, 1d);
        var start = new KeyFrame { Cue = new Cue(0) };
        var end = new KeyFrame { Cue = new Cue(1) };
        start.Setters.Add(from);
        end.Setters.Add(to);

        if (!Reduced)
        {
            start.Setters.Add(TransformSetter(Transform(dx, dy, fromScale)));
            end.Setters.Add(TransformSetter(TransformOperations.Identity));
        }

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(Reduced ? Math.Min(durationMs, 180) : durationMs),
            Delay = TimeSpan.FromMilliseconds(Reduced ? 0 : delayMs),
            Easing = EaseOut,
            FillMode = FillMode.Backward
        };
        animation.Children.Add(start);
        animation.Children.Add(end);
        return animation.RunAsync(target, token);
    }

    /// <summary>A quick scale bump, for the favourite star.</summary>
    public static void Pop(Visual target)
    {
        if (Reduced)
        {
            return;
        }

        var animation = new Animation { Duration = TimeSpan.FromMilliseconds(360), Easing = new SineEaseInOut() };
        foreach (var (cue, scale) in new[] { (0d, 1d), (0.4, 1.35), (1d, 1d) })
        {
            var frame = new KeyFrame { Cue = new Cue(cue) };
            frame.Setters.Add(TransformSetter(Transform(0, 0, scale)));
            animation.Children.Add(frame);
        }
        _ = animation.RunAsync(target);
    }

    /// <summary>
    /// Plays an entrance on a dialog's content once it opens. The OS window itself appears
    /// instantly (native windows can't be animated portably), so the content scales up inside it.
    /// </summary>
    public static T RevealOnOpen<T>(T window) where T : Window
    {
        window.Opened += (_, _) =>
        {
            if (window.Content is Visual content)
            {
                _ = Reveal(content, dy: 8, fromScale: 0.97, durationMs: 220);
            }
        };
        return window;
    }

    /// <summary>
    /// A RenderTransform keyframe. Code-built animations find no animator for RenderTransform
    /// (Avalonia's own TransformOperationsAnimator is internal and only wired up for XAML), and
    /// RunAsync throws "No animator registered" — so each setter carries this one explicitly.
    /// </summary>
    private static Setter TransformSetter(TransformOperations value)
    {
        var setter = new Setter(Visual.RenderTransformProperty, value);
        Animation.SetAnimator(setter, TransformAnimator);
        return setter;
    }

    private static readonly TransformOperationsAnimator TransformAnimator = new();

    private sealed class TransformOperationsAnimator : InterpolatingAnimator<ITransform>
    {
        public override ITransform Interpolate(double progress, ITransform oldValue, ITransform newValue) =>
            TransformOperations.Interpolate(
                oldValue as TransformOperations ?? TransformOperations.Identity,
                newValue as TransformOperations ?? TransformOperations.Identity,
                progress);
    }

    /// <summary>A TransformOperations path, formatted culture-invariantly.</summary>
    public static TransformOperations TranslateY(double y) =>
        TransformOperations.Parse(string.Create(CultureInfo.InvariantCulture, $"translateY({y}px)"));

    private static TransformOperations Transform(double dx, double dy, double scale) =>
        TransformOperations.Parse(string.Create(CultureInfo.InvariantCulture,
            $"translate({dx}px, {dy}px) scale({scale})"));
}
