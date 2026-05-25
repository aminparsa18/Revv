using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv;

// Full SkiaSharp attitude indicator (roll axis only — pitch fixed at 0).
// Call SetRoll(degrees) from the main thread on every steering update.
public sealed class AttitudeSKView : SKCanvasView
{
    private float _rollDeg;

    public AttitudeSKView()
    {
        BackgroundColor  = Colors.Transparent;
        EnableTouchEvents = false;
        InputTransparent  = true;
    }

    public void SetRoll(float degrees)
    {
        _rollDeg = degrees;
        InvalidateSurface();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float W  = e.Info.Width;
        float H  = e.Info.Height;
        float cx = W / 2f;
        float cy = H / 2f;
        float S  = MathF.Min(W, H);
        float R  = S / 2f * 0.96f;
        float bz = R * 0.145f;          // bezel width
        float br = R - bz;              // ball radius

        DrawOuterGlow(canvas, cx, cy, R);
        DrawBezel(canvas, cx, cy, R, br);
        DrawHorizonBall(canvas, cx, cy, br);
        DrawSphereOverlay(canvas, cx, cy, br);
        DrawRollScale(canvas, cx, cy, R, br);
        DrawBankPointer(canvas, cx, cy, R, br);
        DrawAircraftSymbol(canvas, cx, cy, br);
        DrawBezelEdge(canvas, cx, cy, R, br);
    }

    // ── Outer ambient glow ───────────────────────────────────────────────────────
    private static void DrawOuterGlow(SKCanvas canvas, float cx, float cy, float R)
    {
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), R * 1.07f,
            new[]
            {
                SKColors.Transparent,
                SKColors.Transparent,
                new SKColor(0xFF, 0x95, 0x00, 18),
                SKColors.Transparent,
            },
            new[] { 0f, 0.80f, 0.93f, 1.0f },
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.DrawCircle(cx, cy, R * 1.07f, paint);
    }

    // ── Metallic bezel ring ──────────────────────────────────────────────────────
    // Full disc fill — ball content overdraw the center in DrawHorizonBall.
    private static void DrawBezel(SKCanvas canvas, float cx, float cy, float R, float br)
    {
        // Main metallic gradient (dark all around — deeper shadow feel)
        using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            using var sh = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy - R), new SKPoint(cx, cy + R),
                new[]
                {
                    new SKColor(0x32, 0x32, 0x3E),   // darkened top
                    new SKColor(0x22, 0x22, 0x2E),
                    new SKColor(0x16, 0x16, 0x1E),
                    new SKColor(0x10, 0x10, 0x18),
                    new SKColor(0x08, 0x07, 0x06),
                },
                new[] { 0f, 0.18f, 0.50f, 0.80f, 1.0f },
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawCircle(cx, cy, R, paint);
        }

        // Outer dark border ring (hard dark edge, no catchlight)
        using var rim = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f, Color = new SKColor(0x08, 0x08, 0x10, 220),
        };
        canvas.DrawCircle(cx, cy, R - 1.25f, rim);

        // Inner shadow ring (depth at bezel/ball boundary)
        using var innerShadow = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 6f, Color = new SKColor(0, 0, 0, 210),
        };
        canvas.DrawCircle(cx, cy, br + 2.5f, innerShadow);
    }

    // ── Rotating horizon ball ────────────────────────────────────────────────────
    private void DrawHorizonBall(SKCanvas canvas, float cx, float cy, float br)
    {
        canvas.Save();

        using var clip = new SKPath();
        clip.AddCircle(cx, cy, br - 1.5f);
        canvas.ClipPath(clip);

        canvas.RotateDegrees(_rollDeg, cx, cy);

        // Sky — deep royal blue fading to electric blue at horizon
        using (var paint = new SKPaint { IsAntialias = true })
        {
            using var sh = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy - br), new SKPoint(cx, cy + br * 0.15f),
                new[]
                {
                    new SKColor(0x00, 0x38, 0xB8),   // deep royal blue at zenith
                    new SKColor(0x00, 0x62, 0xE8),   // mid electric blue
                    new SKColor(0x00, 0x82, 0xFF),   // bright at horizon
                },
                new[] { 0f, 0.52f, 1.0f },
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawRect(cx - br, cy - br, br * 2f, br * 2f, paint);
        }

        // Ground — dark midnight asphalt, reads as "night track" not dirt
        using (var paint = new SKPaint { IsAntialias = true })
        {
            using var sh = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy), new SKPoint(cx, cy + br),
                new[]
                {
                    new SKColor(0x0D, 0x18, 0x28),   // dark midnight navy at horizon
                    new SKColor(0x06, 0x0C, 0x14),   // near-black navy
                    new SKColor(0x02, 0x04, 0x08),   // OLED near-black
                },
                new[] { 0f, 0.45f, 1.0f },
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawRect(cx - br, cy, br * 2f, br, paint);
        }

        DrawPitchLadder(canvas, cx, cy, br);

        // Horizon glow (soft amber band)
        using (var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0x95, 0x00, 50) })
            canvas.DrawRect(cx - br, cy - 7, br * 2f, 14, paint);

        // Horizon line (amber)
        using (var paint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f, Color = new SKColor(0xFF, 0x95, 0x00),
        })
            canvas.DrawLine(cx - br, cy, cx + br, cy, paint);

        canvas.Restore();
    }

    // ── Pitch ladder ─────────────────────────────────────────────────────────────
    private static void DrawPitchLadder(SKCanvas canvas, float cx, float cy, float br)
    {
        // 2.4 px per degree at ballR=120 puts ±30° marks clearly visible on screen
        float pxPerDeg = br * 0.020f;

        var marks = new (int deg, float halfW)[]
        {
            (-30, 0.46f), (-20, 0.34f), (-10, 0.27f), (-5, 0.15f),
            (5, 0.15f), (10, 0.27f), (20, 0.34f), (30, 0.46f),
        };

        using var linePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(0xFF, 0xFF, 0xFF, 195),
            TextSize = br * 0.092f,
        };

        foreach (var (deg, wf) in marks)
        {
            float y  = cy - deg * pxPerDeg * 2.4f;
            float hw = br * wf;
            bool major = deg % 10 == 0;

            linePaint.StrokeWidth = major ? 1.8f : 1.2f;
            linePaint.Color = new SKColor(0xFF, 0xFF, 0xFF, (byte)(major ? 195 : 125));
            canvas.DrawLine(cx - hw, y, cx + hw, y, linePaint);

            if (major)
            {
                string label = MathF.Abs(deg).ToString();
                float tw = textPaint.MeasureText(label);
                float ty = y + textPaint.TextSize * 0.36f;
                canvas.DrawText(label, cx - hw - tw - 5, ty, textPaint);
                canvas.DrawText(label, cx + hw + 5, ty, textPaint);
            }
        }
    }

    // ── Sphere depth overlay (vignette + specular) ───────────────────────────────
    private static void DrawSphereOverlay(SKCanvas canvas, float cx, float cy, float br)
    {
        // Edge vignette (darkens toward circle boundary → 3D globe feel)
        using var vigShader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), br,
            new[]
            {
                SKColors.Transparent, SKColors.Transparent,
                new SKColor(0, 0, 0, 70),
                new SKColor(0, 0, 0, 160),
            },
            new[] { 0f, 0.68f, 0.86f, 1.0f },
            SKShaderTileMode.Clamp);
        using var vigPaint = new SKPaint { IsAntialias = true, Shader = vigShader };
        canvas.DrawCircle(cx, cy, br, vigPaint);

        // Specular highlight — bright off-center spot at top-left
        using var specShader = SKShader.CreateRadialGradient(
            new SKPoint(cx - br * 0.12f, cy - br * 0.52f), br * 0.68f,
            new[] { new SKColor(0xFF, 0xFF, 0xFF, 22), SKColors.Transparent },
            null, SKShaderTileMode.Clamp);
        using var specPaint = new SKPaint { IsAntialias = true, Shader = specShader };
        canvas.DrawCircle(cx, cy, br, specPaint);
    }

    // ── Roll scale — full 360° tick ring on bezel inner face ─────────────────────
    private static void DrawRollScale(SKCanvas canvas, float cx, float cy, float R, float br)
    {
        float bz = R - br;

        using var paint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        };

        for (int deg = 0; deg < 360; deg += 5)
        {
            bool major = deg % 30 == 0;
            bool mid   = deg % 10 == 0 && !major;
            // remainder are fine 5° ticks

            float lenFrac = major ? 0.58f : mid ? 0.34f : 0.18f;
            float thick   = major ? 2.2f  : mid ? 1.3f  : 0.85f;
            byte  alpha   = major ? (byte)205 : mid ? (byte)160 : (byte)95;

            float rad = (deg - 90f) * MathF.PI / 180f;
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);

            float midR = br + bz * 0.5f;          // midpoint of bezel ring
            float half = bz * lenFrac * 0.5f;
            float r1 = midR - half;
            float r2 = midR + half;

            paint.StrokeWidth = thick;
            paint.Color = new SKColor(0xCC, 0xCC, 0xDC, alpha);
            canvas.DrawLine(cx + cos * r1, cy + sin * r1, cx + cos * r2, cy + sin * r2, paint);
        }
    }

    // ── Bank pointer: fixed white reference + rotating amber marker ───────────────
    private void DrawBankPointer(SKCanvas canvas, float cx, float cy, float R, float br)
    {
        float bz = R - br;

        // Rotating amber marker (moves with roll, reads against fixed scale)
        canvas.Save();
        canvas.RotateDegrees(_rollDeg, cx, cy);
        float rW = bz * 0.46f;
        float rH = bz * 0.50f;
        using (var path = new SKPath())
        {
            // Triangle: tip points toward ball (downward at 12-o'clock)
            path.MoveTo(cx, cy - br + 1f);
            path.LineTo(cx - rW / 2, cy - br - rH);
            path.LineTo(cx + rW / 2, cy - br - rH);
            path.Close();
            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0xFF, 0xA0, 0x00, 225) };
            canvas.DrawPath(path, fill);
        }
        canvas.Restore();

        // Fixed white reference index at 0° (drawn after so it sits on top at rest)
        float fW = bz * 0.40f;
        float fH = bz * 0.38f;
        float fOuter = cy - R + bz * 0.14f;
        float fInner = cy - br;
        using (var path = new SKPath())
        {
            path.MoveTo(cx, fInner);
            path.LineTo(cx - fW / 2, fOuter);
            path.LineTo(cx + fW / 2, fOuter);
            path.Close();
            using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0xFF, 0xFF, 0xFF, 230) };
            canvas.DrawPath(path, fill);
        }
    }

    // ── Aircraft symbol (fixed, amber) ───────────────────────────────────────────
    private static void DrawAircraftSymbol(SKCanvas canvas, float cx, float cy, float br)
    {
        float armLen  = br * 0.30f;
        float armGap  = br * 0.13f;
        float thick   = br * 0.030f;
        float ringR   = br * 0.072f;
        float dotR    = br * 0.044f;
        var   amber   = new SKColor(0xFF, 0x95, 0x00);

        using var paint = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round };

        // Wing arms
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = thick;
        paint.Color = amber;
        canvas.DrawLine(cx - armGap - armLen, cy, cx - armGap, cy, paint);
        canvas.DrawLine(cx + armGap, cy, cx + armGap + armLen, cy, paint);

        // Center fuselage nub (downward tick)
        paint.StrokeWidth = thick * 0.80f;
        canvas.DrawLine(cx, cy, cx, cy + dotR * 2.2f, paint);

        // Center ring
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = thick * 0.65f;
        canvas.DrawCircle(cx, cy, ringR, paint);

        // Center dot fill
        paint.Style = SKPaintStyle.Fill;
        canvas.DrawCircle(cx, cy, dotR, paint);
    }

    // ── Bezel inner edge / outer rim lines ───────────────────────────────────────
    private static void DrawBezelEdge(SKCanvas canvas, float cx, float cy, float R, float br)
    {
        // Hard shadow ring at ball/bezel junction
        using var shadow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, Color = new SKColor(0, 0, 0, 210) };
        canvas.DrawCircle(cx, cy, br, shadow);

        // Faint amber accent just inside the junction (REVV brand accent)
        using var accent = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(0xFF, 0x95, 0x00, 38) };
        canvas.DrawCircle(cx, cy, br - 2f, accent);

        // Very subtle dark rim (no bright highlight — keeps border heavy/dark)
        using var outer = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(0x18, 0x18, 0x22, 120) };
        canvas.DrawCircle(cx, cy, R - 1f, outer);
    }
}
