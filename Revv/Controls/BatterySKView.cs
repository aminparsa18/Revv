using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv.Controls;

// Semicircular battery gauge.
// Fill gradient is FIXED: red (empty/left) → amber (mid) → green (full/right).
// Only the fill LENGTH changes with level, so low battery = red sliver,
// full battery = full green arc. Color and length together communicate state.
public sealed class BatterySKView : SKCanvasView
{
    private float _level    = -1f;   // -1 = unknown, 0..1
    private bool  _charging;

    // Detailed bolt outline from the SVG (viewBox 0 0 32 32)
    private static readonly SKPath _boltPath = SKPath.ParseSvgPathData(
        "M18.606 0.023c-0.054 0-0.108 0.002-0.161 0.006-0.353 0.028-0.587 0.147-0.864 0.333" +
        "-0.154 0.102-0.295 0.228-0.419 0.373-0.037 0.043-0.071 0.088-0.103 0.134" +
        "l-11.207 14.832c-0.442 0.607-0.508 1.407-0.168 2.076s1.026 1.093 1.779 1.099" +
        "l5.773 0.042-1.815 10.694c-0.172 0.919 0.318 1.835 1.18 2.204" +
        "c0.257 0.11 0.527 0.163 0.793 0.163 0.629 0 1.145-0.294 1.533-0.825" +
        "l11.22-16.072c0.442-0.607 0.507-1.408 0.168-2.076-0.34-0.669-1.026-1.093-1.779-1.098" +
        "l-5.773-0.010 1.796-9.402c0.038-0.151 0.057-0.308 0.057-0.47" +
        " 0-1.082-0.861-1.964-1.939-1.999-0.024-0.001-0.047-0.001-0.071-0.001z");

    // Gradient stops shared between fill shader and label/icon colour sampling
    private static readonly (float pos, SKColor color)[] GradientStops =
    [
        (0.00f, new SKColor(0xE8, 0x00, 0x1D)),   // red   — empty
        (0.25f, new SKColor(0xFF, 0x55, 0x00)),   // orange-red
        (0.50f, new SKColor(0xFF, 0x95, 0x00)),   // amber
        (0.75f, new SKColor(0xA8, 0xD4, 0x00)),   // yellow-green
        (1.00f, new SKColor(0x00, 0xE8, 0x7A)),   // green — full
    ];

    public BatterySKView()
    {
        BackgroundColor   = Colors.Transparent;
        EnableTouchEvents = false;
        InputTransparent  = true;
    }

    public void SetBattery(float level, bool charging)
    {
        _level    = level;
        _charging = charging;
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

        float Ro     = MathF.Min(cx * 0.86f, H * 0.91f);
        float trackW = Ro * 0.24f;
        float midR   = Ro - (trackW / 2f);

        // Shift center down by half a track-width so the round end-caps
        // sit exactly at the canvas bottom edge instead of being clipped.
        float cy = H - (trackW * 0.5f);

        float level     = _level < 0 ? 0f : MathF.Min(_level, 1f);
        float fillSweep = 180f * level;

        SKColor accent = LevelColor(level, _level < 0);

        DrawTrack(canvas, cx, cy, midR, trackW);
        if (_level >= 0 && level > 0.005f)
        {
            DrawFill(canvas, cx, cy, midR, trackW, fillSweep);
            DrawTipGlow(canvas, cx, cy, midR, fillSweep, accent);
        }
        DrawBoltIcon(canvas, cx, cy, Ro);
    }

    // ── Colour sampled from the same gradient the fill uses ──────────────────

    private static SKColor LevelColor(float level, bool unknown)
    {
        if (unknown)
        {
            return new SKColor(0x44, 0x44, 0x44);
        }

        level = Math.Clamp(level, 0f, 1f);
        for (int i = 0; i < GradientStops.Length - 1; i++)
        {
            (float p0, SKColor c0) = GradientStops[i];
            (float p1, SKColor c1) = GradientStops[i + 1];
            if (level <= p1)
            {
                float t = (level - p0) / (p1 - p0);
                return new SKColor(
                    (byte)(c0.Red   + ((c1.Red   - c0.Red)   * t)),
                    (byte)(c0.Green + ((c1.Green - c0.Green)  * t)),
                    (byte)(c0.Blue  + ((c1.Blue  - c0.Blue)   * t)));
            }
        }
        return GradientStops[^1].color;
    }

    // ── Geometry helpers ─────────────────────────────────────────────────────

    private static SKPoint Polar(float cx, float cy, float r, float deg)
    {
        float rad = deg * MathF.PI / 180f;
        return new SKPoint(cx + (r * MathF.Cos(rad)), cy + (r * MathF.Sin(rad)));
    }

    private static SKRect ArcOval(float cx, float cy, float r)
        => new(cx - r, cy - r, cx + r, cy + r);

    // ── Background arc — always shows full gauge range ────────────────────────

    private static void DrawTrack(SKCanvas c, float cx, float cy, float midR, float trackW)
    {
        using SKPaint paint = new()
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = trackW, StrokeCap = SKStrokeCap.Round,
            Color = new SKColor(0x28, 0x28, 0x28),
        };
        c.DrawArc(ArcOval(cx, cy, midR), 180f, 180f, false, paint);

        // Inset shadow ring for depth
        float innerR = midR - (trackW / 2f) + 1.5f;
        using SKPaint shadow = new()
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f, Color = new SKColor(0, 0, 0, 110),
        };
        c.DrawArc(ArcOval(cx, cy, innerR), 180f, 180f, false, shadow);
    }

    // ── Coloured fill — fixed gradient, variable length ───────────────────────

    private static void DrawFill(SKCanvas c, float cx, float cy, float midR, float trackW, float sweep)
    {
        using SKPaint paint = new()
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = trackW, StrokeCap = SKStrokeCap.Round,
        };

        // Gradient spans the full arc width regardless of current fill length.
        // Red is always at the left (empty) end, green at the right (full) end.
        SKColor[] colors = new SKColor[GradientStops.Length];
        float[] stops  = new float[GradientStops.Length];
        for (int i = 0; i < GradientStops.Length; i++)
        {
            colors[i] = new SKColor(
                GradientStops[i].color.Red,
                GradientStops[i].color.Green,
                GradientStops[i].color.Blue, 230);
            stops[i] = GradientStops[i].pos;
        }

        using SKShader sh = SKShader.CreateLinearGradient(
            new SKPoint(cx - midR, cy),
            new SKPoint(cx + midR, cy),
            colors, stops, SKShaderTileMode.Clamp);
        paint.Shader = sh;
        c.DrawArc(ArcOval(cx, cy, midR), 180f, sweep, false, paint);
    }

    // ── Soft glow at fill tip ─────────────────────────────────────────────────

    private static void DrawTipGlow(SKCanvas c, float cx, float cy, float midR, float sweep, SKColor accent)
    {
        SKPoint tip   = Polar(cx, cy, midR, 180f + sweep);
        float glowR = midR * 0.24f;

        using SKPaint paint = new() { IsAntialias = true };
        using SKShader sh = SKShader.CreateRadialGradient(
            tip, glowR,
            [
                new SKColor(accent.Red, accent.Green, accent.Blue, 115),
                new SKColor(accent.Red, accent.Green, accent.Blue, 0),
            ],
            null, SKShaderTileMode.Clamp);
        paint.Shader = sh;
        c.DrawCircle(tip.X, tip.Y, glowR, paint);
    }

    // ── Bolt icon — scaled SVG path, fixed muted-amber colour ────────────────

    private static void DrawBoltIcon(SKCanvas c, float cx, float cy, float Ro)
    {
        float iconH = Ro * 0.32f;
        float iconY = cy - (Ro * 0.22f);

        _boltPath.GetTightBounds(out SKRect b);
        float scale = iconH / b.Height;
        float tx    = cx    - ((b.Left + (b.Width  / 2f)) * scale);
        float ty    = iconY - ((b.Top  + (b.Height / 2f)) * scale);

        using SKPaint fill = new()
        {
            IsAntialias = true,
            Color       = new SKColor(0xFF, 0x95, 0x00, 150),   // HorizonAmber, dimmed
        };

        c.Save();
        c.Translate(tx, ty);
        c.Scale(scale);
        c.DrawPath(_boltPath, fill);
        c.Restore();
    }
}