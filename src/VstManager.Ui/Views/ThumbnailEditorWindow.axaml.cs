using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace VstManager.Ui.Views;

/// <summary>
/// Crop / zoom / pan a plugin thumbnail inside a square. Returns the path of a PNG holding
/// exactly what was inside the square, or null if cancelled; the caller stages it like any
/// other local image, so nothing is written until the detail window's Save.
/// </summary>
public partial class ThumbnailEditorWindow : Window
{
    private const double MaxZoom = 8;
    private const int OutputPixels = 512;

    private readonly Bitmap? _source;
    private double _fitScale = 1;
    private double _scale = 1;
    private double _x;
    private double _y;
    private Point? _dragFrom;
    private bool _syncingSlider;

    public ThumbnailEditorWindow() => InitializeComponent();

    private ThumbnailEditorWindow(Bitmap source) : this()
    {
        _source = source;
        Picture.Source = source;
        Opened += (_, _) => FitWhole();
    }

    public static async Task<string?> EditAsync(Window owner, Bitmap source) =>
        await new ThumbnailEditorWindow(source).ShowDialog<string?>(owner);

    private double FrameSize => Stage.Bounds.Width > 0 ? Stage.Bounds.Width : Stage.Width;

    private Size SourceSize => _source?.Size ?? new Size(1, 1);

    private void FitWhole()
    {
        var size = FrameSize;
        _fitScale = Math.Min(size / SourceSize.Width, size / SourceSize.Height);
        SetScale(_fitScale, new Point(size / 2, size / 2), recentre: true);
    }

    private void Fill()
    {
        var size = FrameSize;
        var fill = Math.Max(size / SourceSize.Width, size / SourceSize.Height);
        SetScale(fill, new Point(size / 2, size / 2), recentre: true);
    }

    /// <summary>Zooms around <paramref name="anchor"/>, so the point under the cursor stays put.</summary>
    private void SetScale(double scale, Point anchor, bool recentre = false)
    {
        scale = Math.Clamp(scale, _fitScale, _fitScale * MaxZoom);

        if (recentre)
        {
            var size = FrameSize;
            _x = (size - SourceSize.Width * scale) / 2;
            _y = (size - SourceSize.Height * scale) / 2;
        }
        else
        {
            var ratio = scale / _scale;
            _x = anchor.X - (anchor.X - _x) * ratio;
            _y = anchor.Y - (anchor.Y - _y) * ratio;
        }

        _scale = scale;
        Layout();

        _syncingSlider = true;
        ZoomSlider.Value = _scale / _fitScale;
        _syncingSlider = false;
    }

    /// <summary>
    /// Keeps the image covering as much of the square as it can: a side larger than the square
    /// can't expose an empty edge, a side smaller than it stays fully inside.
    /// </summary>
    private void Layout()
    {
        var size = FrameSize;
        var width = SourceSize.Width * _scale;
        var height = SourceSize.Height * _scale;

        _x = width <= size ? Math.Clamp(_x, 0, size - width) : Math.Clamp(_x, size - width, 0);
        _y = height <= size ? Math.Clamp(_y, 0, size - height) : Math.Clamp(_y, size - height, 0);

        Picture.Width = width;
        Picture.Height = height;
        Canvas.SetLeft(Picture, _x);
        Canvas.SetTop(Picture, _y);
    }

    private void Frame_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dragFrom = e.GetPosition(Stage);
        e.Pointer.Capture(Frame);
    }

    private void Frame_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragFrom is not { } from)
        {
            return;
        }

        var now = e.GetPosition(Stage);
        _x += now.X - from.X;
        _y += now.Y - from.Y;
        _dragFrom = now;
        Layout();
    }

    private void Frame_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragFrom = null;
        e.Pointer.Capture(null);
    }

    private void Frame_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        var factor = e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        SetScale(_scale * factor, e.GetPosition(Stage));
        e.Handled = true;
    }

    private void ZoomSlider_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_syncingSlider || _source is null)
        {
            return;
        }

        var size = FrameSize;
        SetScale(_fitScale * e.NewValue, new Point(size / 2, size / 2));
    }

    private void Fit_Click(object? sender, RoutedEventArgs e) => FitWhole();

    private void Fill_Click(object? sender, RoutedEventArgs e) => Fill();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void Apply_Click(object? sender, RoutedEventArgs e)
    {
        var size = FrameSize;
        var dpi = 96 * OutputPixels / size;

        // Rendered at 2x the on-screen square so the saved thumbnail stays sharp on HiDPI.
        using var output = new RenderTargetBitmap(new PixelSize(OutputPixels, OutputPixels), new Vector(dpi, dpi));
        output.Render(Stage);

        var path = Path.Combine(Path.GetTempPath(), $"vstmanager-thumb-{Guid.NewGuid():N}.png");
        output.Save(path);
        Close(path);
    }
}
