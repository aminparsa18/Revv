using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv.Controls;

public sealed class ClusterFrameSKView : SKCanvasView
{
    private SKBitmap? _bitmap;

    public ClusterFrameSKView()
    {
        BackgroundColor   = Colors.Transparent;
        EnableTouchEvents = false;
        InputTransparent  = true;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        using Stream stream = await FileSystem.OpenAppPackageFileAsync("frame.png");
        _bitmap = SKBitmap.Decode(stream);
        InvalidateSurface();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (_bitmap is null)
        {
            return;
        }

        float W = e.Info.Width;
        float H = e.Info.Height;

        using SKPaint paint = new() { IsAntialias = true };
        using SKImage image = SKImage.FromBitmap(_bitmap);
        canvas.DrawImage(image, new SKRect(0, 0, W, H), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
    }
}
