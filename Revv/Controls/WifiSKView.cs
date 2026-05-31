using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv.Controls;

// WiFi signal indicator drawn as three concentric arcs + center dot.
// Level 0 = no signal / disabled, 1-3 = progressively stronger.
// Arc geometry: startAngle=210°, sweepAngle=120° (clockwise), peaking at 270° (top).
// All arcs are centered at the dot position (bottom-center of the canvas).
public sealed class WifiSKView : SKCanvasView
{
    private int _level = -1; // -1 = uninitialized, 0-3 = signal level

    public WifiSKView()
    {
        BackgroundColor   = Colors.Transparent;
        EnableTouchEvents = false;
        InputTransparent  = true;
    }

    public void SetSignal(int level)
    {
        _level = Math.Clamp(level, 0, 3);
        InvalidateSurface();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float W  = e.Info.Width;
        float H  = e.Info.Height;
        float cx = W / 2f;

        float r3      = MathF.Min(W * 0.44f, H * 0.50f);
        float r2      = r3 * 0.65f;
        float r1      = r3 * 0.38f;
        float dotR    = r3 * 0.14f;
        float strokeW = r3 * 0.20f;
        // Place center (dot) so outer arc top and dot bottom have equal canvas margins.
        float cy = (H + r3) / 2f;

        bool unknown = _level < 0;
        SKColor active  = new(0x00, 0xE8, 0x7A);   // ConnectedGreen
        SKColor dim     = new(0x28, 0x28, 0x28);   // SurfaceHigh-ish

        using SKPaint arcPaint = new()
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeCap   = SKStrokeCap.Round,
            StrokeWidth = strokeW,
        };
        using SKPaint dotPaint = new() { IsAntialias = true };

        arcPaint.Color = (!unknown && _level >= 3) ? active : dim;
        DrawArc(canvas, cx, cy, r3, arcPaint);

        arcPaint.Color = (!unknown && _level >= 2) ? active : dim;
        DrawArc(canvas, cx, cy, r2, arcPaint);

        arcPaint.Color = (!unknown && _level >= 1) ? active : dim;
        DrawArc(canvas, cx, cy, r1, arcPaint);

        dotPaint.Color = (!unknown && _level >= 1) ? active : dim;
        canvas.DrawCircle(cx, cy, dotR, dotPaint);
    }

    private static void DrawArc(SKCanvas c, float cx, float cy, float r, SKPaint p)
        => c.DrawArc(new SKRect(cx - r, cy - r, cx + r, cy + r), 210f, 120f, false, p);
}
