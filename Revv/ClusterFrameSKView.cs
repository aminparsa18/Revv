using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv;

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
        using var stream = await FileSystem.OpenAppPackageFileAsync("frame.png");
        _bitmap = SKBitmap.Decode(stream);
        InvalidateSurface();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        if (_bitmap is null) return;

        float W = e.Info.Width;
        float H = e.Info.Height;

        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        canvas.DrawBitmap(_bitmap, new SKRect(0, 0, W, H), paint);
    }
}
