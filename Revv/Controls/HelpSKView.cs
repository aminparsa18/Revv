using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv.Controls;

public sealed class HelpSKView : SKCanvasView
{
    public event EventHandler? Clicked;

    private bool _pressed;

    // ? stem — SVG path data from a 24×24 icon
    private static readonly SKPath _questionPath = SKPath.ParseSvgPathData(
        "M10.125 8.875" +
        "C10.125 7.83947 10.9645 7 12 7" +
        "C13.0355 7 13.875 7.83947 13.875 8.875" +
        "C13.875 9.56245 13.505 10.1635 12.9534 10.4899" +
        "C12.478 10.7711 12 11.1977 12 11.75V13");

    public HelpSKView()
    {
        BackgroundColor   = Colors.Transparent;
        EnableTouchEvents = true;
    }

    protected override void OnTouch(SKTouchEventArgs e)
    {
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                _pressed = true;
                InvalidateSurface();
                e.Handled = true;
                break;

            case SKTouchAction.Released:
                if (_pressed)
                {
                    _pressed = false;
                    InvalidateSurface();
                    MainThread.BeginInvokeOnMainThread(() => Clicked?.Invoke(this, EventArgs.Empty));
                }
                e.Handled = true;
                break;

            case SKTouchAction.Cancelled:
            case SKTouchAction.Exited:
                _pressed = false;
                InvalidateSurface();
                break;
        }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float w  = e.Info.Width;
        float h  = e.Info.Height;
        float cx = w / 2f;
        float cy = h / 2f;

        // Scale the 24×24 SVG coordinate space to fill this canvas
        float scale = MathF.Min(w, h) / 24f;

        byte alpha = _pressed ? (byte)230 : (byte)140;
        SKColor color = new(0x99, 0x99, 0x99, alpha);

        canvas.Save();
        canvas.Translate(cx - 12f * scale, cy - 12f * scale);
        canvas.Scale(scale);

        using (SKPaint paint = new()
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color       = color,
        })
        {
            canvas.DrawCircle(12, 12, 10, paint);
        }

        using (SKPaint paint = new()
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            StrokeCap   = SKStrokeCap.Round,
            Color       = color,
        })
        {
            canvas.DrawPath(_questionPath, paint);
        }

        using (SKPaint paint = new()
        {
            IsAntialias = true,
            Color       = color,
        })
        {
            canvas.DrawCircle(12, 16, 1, paint);
        }

        canvas.Restore();
    }
}
