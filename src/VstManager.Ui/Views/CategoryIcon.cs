using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using VstManager.Core.Services;

namespace VstManager.Ui.Views;

/// <summary>
/// A category's icon at a given size: the vector piano keys / knob for the built-in Instrument and
/// Effect categories, the category's own emoji for user-made ones. Tinted with the surrounding
/// text colour, so it follows selected/hover states like the label next to it.
/// </summary>
public class CategoryIcon : Decorator
{
    public static readonly StyledProperty<string?> CategoryIdProperty =
        AvaloniaProperty.Register<CategoryIcon, string?>(nameof(CategoryId));

    public static readonly StyledProperty<string?> EmojiProperty =
        AvaloniaProperty.Register<CategoryIcon, string?>(nameof(Emoji));

    public static readonly StyledProperty<double> IconSizeProperty =
        AvaloniaProperty.Register<CategoryIcon, double>(nameof(IconSize), 16);

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<CategoryIcon>();

    static CategoryIcon()
    {
        CategoryIdProperty.Changed.AddClassHandler<CategoryIcon>((c, _) => c.Rebuild());
        EmojiProperty.Changed.AddClassHandler<CategoryIcon>((c, _) => c.Rebuild());
        IconSizeProperty.Changed.AddClassHandler<CategoryIcon>((c, _) => c.Rebuild());
    }

    public CategoryIcon()
    {
        VerticalAlignment = VerticalAlignment.Center;
    }

    public string? CategoryId
    {
        get => GetValue(CategoryIdProperty);
        set => SetValue(CategoryIdProperty, value);
    }

    public string? Emoji
    {
        get => GetValue(EmojiProperty);
        set => SetValue(EmojiProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private void Rebuild()
    {
        var (stroke, fill) = CategoryId switch
        {
            PresetCategories.Instrument => ("CategoryInstrumentStroke", "CategoryInstrumentFill"),
            PresetCategories.Effect => ("CategoryEffectStroke", (string?)null),
            _ => (null, null)
        };

        if (stroke is null)
        {
            Child = new TextBlock
            {
                Text = Emoji,
                FontSize = IconSize,
                VerticalAlignment = VerticalAlignment.Center
            };
            return;
        }

        var size = IconSize;
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(MakePath(stroke, filled: false, size));
        if (fill is not null)
        {
            grid.Children.Add(MakePath(fill, filled: true, size));
        }
        Child = grid;
    }

    /// <summary>
    /// The geometries live on a 24-unit grid; a 24×24 Viewbox keeps both parts aligned and scales
    /// the stroke with the icon, so small sizes thin out proportionally like the canvas showed.
    /// </summary>
    private Control MakePath(string key, bool filled, double size)
    {
        var path = new Avalonia.Controls.Shapes.Path
        {
            Data = Application.Current?.FindResource(key) as Geometry,
            StrokeThickness = size <= 14 ? 2.0 : 1.9,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
        var tint = new Binding(nameof(Foreground)) { Source = this };
        path.Bind(filled ? Avalonia.Controls.Shapes.Shape.FillProperty : Avalonia.Controls.Shapes.Shape.StrokeProperty, tint);

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
