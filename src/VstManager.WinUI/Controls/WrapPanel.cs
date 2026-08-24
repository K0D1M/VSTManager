using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace VstManager.WinUI.Controls;

/// <summary>
/// A left-to-right wrapping panel that sizes each child to its own desired width.
///
/// Exists because stock WinUI has no WrapPanel. The obvious substitute,
/// UniformGridLayout, makes every cell the width of the *first* realised item — which silently
/// truncates any longer one, so a row of tag chips renders as "Analo", "Samp", "Limite". A
/// wrap panel is ~40 lines, so this is cheaper and more predictable than taking a dependency on
/// the Community Toolkit for one control.
/// </summary>
public partial class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(6.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(6.0, OnLayoutPropertyChanged));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        (d as WrapPanel)?.InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        // An unconstrained width (inside a horizontally-autosizing parent) would put everything on
        // one line; that's the correct wrap-panel behaviour, so it's left alone.
        var lineWidth = 0.0;
        var lineHeight = 0.0;
        var totalWidth = 0.0;
        var totalHeight = 0.0;

        foreach (var child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            var desired = child.DesiredSize;

            var needed = lineWidth == 0 ? desired.Width : lineWidth + HorizontalSpacing + desired.Width;

            if (needed > availableSize.Width && lineWidth > 0)
            {
                totalWidth = Math.Max(totalWidth, lineWidth);
                totalHeight += lineHeight + VerticalSpacing;
                lineWidth = desired.Width;
                lineHeight = desired.Height;
            }
            else
            {
                lineWidth = needed;
                lineHeight = Math.Max(lineHeight, desired.Height);
            }
        }

        totalWidth = Math.Max(totalWidth, lineWidth);
        totalHeight += lineHeight;

        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        var y = 0.0;
        var lineHeight = 0.0;

        foreach (var child in Children)
        {
            var desired = child.DesiredSize;

            if (x > 0 && x + desired.Width > finalSize.Width)
            {
                x = 0;
                y += lineHeight + VerticalSpacing;
                lineHeight = 0;
            }

            child.Arrange(new Rect(x, y, desired.Width, desired.Height));

            x += desired.Width + HorizontalSpacing;
            lineHeight = Math.Max(lineHeight, desired.Height);
        }

        return finalSize;
    }
}
