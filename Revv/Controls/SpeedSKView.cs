using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv.Controls;

// Speedometer gauge drawn as a cubic bezier.
// Curve starts at left-center (lower-left area), bows downward (concave up),
// and sweeps up to upper-center — matching the user's sketch exactly.
// SKPathMeasure slices the bezier for the speed-proportional fill.
public sealed class SpeedSKView : SKCanvasView
{
    private int _speedKmh = 0;

    public SpeedSKView()
    {
        BackgroundColor   = Colors.Transparent;
        EnableTouchEvents = false;
        InputTransparent  = true;
    }

    public void SetSpeed(int kmh)
    {
        int clamped = Math.Clamp(kmh, 0, 300);
        if (_speedKmh == clamped)
        {
            return;
        }

        _speedKmh = clamped;
        InvalidateSurface();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float W = e.Info.Width;
        float H = e.Info.Height;

        float strokeW = MathF.Min(W, H) * 0.09f;
        float m       = strokeW * 0.6f;

        // Explicit start and end points
        SKPoint A = new(m,          H * 0.80f);  // lower-left
        SKPoint B = new(W * 0.60f,  m);           // upper-right

        // Elliptical arc from A to B.
        // Clockwise + Small = the arc that bows DOWNWARD between the two points.
        using SKPath arcPath = new();
        arcPath.MoveTo(A);
        arcPath.ArcTo(
            rx: W * 0.75f, ry: H * 0.65f,
            xAxisRotate: 0,
            largeArc: SKPathArcSize.Small,
            sweep: SKPathDirection.CounterClockwise,
            x: B.X, y: B.Y);

        // ── Track ─────────────────────────────────────────────────────────────
        using SKPaint trackPaint = new()
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeCap   = SKStrokeCap.Round,
            StrokeWidth = strokeW,
            Color       = new SKColor(0x1F, 0x1F, 0x1F),
        };
        canvas.DrawPath(arcPath, trackPaint);

        // ── Fill ──────────────────────────────────────────────────────────────
        if (_speedKmh > 0)
        {
            using SKPathMeasure measure = new(arcPath);
            float fillLen = measure.Length * (_speedKmh / 300f);

            SKPath fillPath = new();
            measure.GetSegment(0f, fillLen, fillPath, true);

            measure.GetMatrix(fillLen, out SKMatrix tipMatrix, SKPathMeasureMatrixFlags.GetPosition);
            SKPoint tip = new(tipMatrix.TransX, tipMatrix.TransY);

            using SKPaint fillPaint = new()
            {
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                StrokeCap   = SKStrokeCap.Round,
                StrokeWidth = strokeW,
            };
            using SKShader fillShader = SKShader.CreateLinearGradient(
                A, B,
                [new SKColor(0xE8, 0x00, 0x1D), new SKColor(0xFF, 0x95, 0x00)],
                null, SKShaderTileMode.Clamp);
            fillPaint.Shader = fillShader;
            canvas.DrawPath(fillPath, fillPaint);

            float glowR = strokeW * 1.1f;
            using SKPaint glowPaint  = new() { IsAntialias = true };
            using SKShader glowShader = SKShader.CreateRadialGradient(
                tip, glowR,
                [new SKColor(0xFF, 0x95, 0x00, 110), new SKColor(0xFF, 0x95, 0x00, 0)],
                null, SKShaderTileMode.Clamp);
            glowPaint.Shader = glowShader;
            canvas.DrawCircle(tip.X, tip.Y, glowR, glowPaint);
        }

        // ── Number + unit ─────────────────────────────────────────────────────
        using SKTypeface boldFace = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        using SKFont numFont  = new(boldFace, H * 0.22f);
        using SKFont unitFont = new(SKTypeface.Default, H * 0.10f);

        using SKPaint numPaint = new() { IsAntialias = true, Color = new SKColor(0xF0, 0xF0, 0xF0) };
        canvas.DrawText(_speedKmh.ToString(), W * 0.52f, H * 0.82f, numFont, numPaint);

        using SKPaint unitPaint = new() { IsAntialias = true, Color = new SKColor(0x55, 0x55, 0x55) };
        canvas.DrawText("km/h", W * 0.52f, H * 0.94f, unitFont, unitPaint);
    }
}
