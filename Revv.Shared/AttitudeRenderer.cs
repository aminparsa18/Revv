using SkiaSharp;

namespace Revv.Shared;

// Pure Skia drawing logic — no platform dependency.
// Call Draw() from any SkiaSharp host: SKCanvasView (MAUI) or SKControl (WinForms).
public static class AttitudeRenderer
{
    public static void Draw(SKCanvas canvas, float W, float H, float rollDeg)
    {
        canvas.Clear(new SKColor(0x0A, 0x0A, 0x0A));

        float cx = W / 2f;
        float cy = H / 2f;
        float S  = MathF.Min(W, H);
        float R  = S / 2f * 0.96f;
        float bz = R * 0.145f;
        float br = R - bz;

        DrawOuterGlow(canvas, cx, cy, R);
        DrawBezel(canvas, cx, cy, R, br);
        DrawHorizonBall(canvas, cx, cy, br, rollDeg);
        DrawSphereOverlay(canvas, cx, cy, br);
        DrawRollScale(canvas, cx, cy, R, br);
        DrawBankPointer(canvas, cx, cy, R, br, rollDeg);
        DrawAircraftSymbol(canvas, cx, cy, br);
        DrawBezelEdge(canvas, cx, cy, R, br);
    }

    public static void DrawOuterGlow(SKCanvas canvas, float cx, float cy, float R)
    {
        using var shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), R * 1.07f,
            new[]
            {
                SKColors.Transparent,
                SKColors.Transparent,
                new SKColor(0xE8, 0x00, 0x1D, 18),
                SKColors.Transparent,
            },
            new[] { 0f, 0.80f, 0.93f, 1.0f },
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.DrawCircle(cx, cy, R * 1.07f, paint);
    }

    public static void DrawBezel(SKCanvas canvas, float cx, float cy, float R, float br)
    {
        using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            using var sh = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy - R), new SKPoint(cx, cy + R),
                new[]
                {
                    new SKColor(0x32, 0x32, 0x3E),
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

        using var rim = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f, Color = new SKColor(0x08, 0x08, 0x10, 220),
        };
        canvas.DrawCircle(cx, cy, R - 1.25f, rim);

        using var innerShadow = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 6f, Color = new SKColor(0, 0, 0, 210),
        };
        canvas.DrawCircle(cx, cy, br + 2.5f, innerShadow);
    }

    // The inner content of the horizon ball — drawn without rotation applied.
    // Callers that cache this should apply clip + RotateDegrees before drawing.
    public static void DrawHorizonContent(SKCanvas canvas, float cx, float cy, float br)
    {
        using (var paint = new SKPaint { IsAntialias = true })
        {
            using var sh = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy - br), new SKPoint(cx, cy + br * 0.15f),
                new[]
                {
                    new SKColor(0x00, 0x38, 0xB8),
                    new SKColor(0x00, 0x62, 0xE8),
                    new SKColor(0x00, 0x82, 0xFF),
                },
                new[] { 0f, 0.52f, 1.0f },
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawRect(cx - br, cy - br, br * 2f, br * 2f, paint);
        }

        using (var paint = new SKPaint { IsAntialias = true })
        {
            using var sh = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy), new SKPoint(cx, cy + br),
                new[]
                {
                    new SKColor(0x0D, 0x18, 0x28),
                    new SKColor(0x06, 0x0C, 0x14),
                    new SKColor(0x02, 0x04, 0x08),
                },
                new[] { 0f, 0.45f, 1.0f },
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawRect(cx - br, cy, br * 2f, br, paint);
        }

        DrawPitchLadder(canvas, cx, cy, br);

        using (var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF, 0x95, 0x00, 50) })
            canvas.DrawRect(cx - br, cy - 7, br * 2f, 14, paint);

        using (var paint = new SKPaint
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f, Color = new SKColor(0xFF, 0x95, 0x00),
        })
            canvas.DrawLine(cx - br, cy, cx + br, cy, paint);
    }

    public static void DrawSphereOverlay(SKCanvas canvas, float cx, float cy, float radius)
    {
        // =========================================================
        // EDGE VIGNETTE
        // Simulates curvature shadowing toward sphere boundaries.
        // Avoid harsh rings — transitions must stay very soft.
        // =========================================================

        using var edgeShader = SKShader.CreateRadialGradient(
            center: new SKPoint(cx, cy),
            radius: radius,
            colors: new[]
            {
            SKColors.Transparent,
            SKColors.Transparent,
            new SKColor(0, 0, 0, 40),
            new SKColor(0, 0, 0, 110),
            },
            colorPos: new[]
            {
            0.00f,
            0.72f,
            0.90f,
            1.00f
            },
            mode: SKShaderTileMode.Clamp);

        using var edgePaint = new SKPaint
        {
            IsAntialias = true,
            Shader = edgeShader,
            BlendMode = SKBlendMode.Multiply,
            FilterQuality = SKFilterQuality.High,
        };

        canvas.DrawCircle(cx, cy, radius, edgePaint);


        // =========================================================
        // PRIMARY SPHERICAL HIGHLIGHT
        // Large soft directional lighting from upper-left.
        // Offset highlight center is CRITICAL for 3D appearance.
        // =========================================================

        using var highlightShader = SKShader.CreateRadialGradient(
            center: new SKPoint(
                cx - radius * 0.28f,
                cy - radius * 0.42f),
            radius: radius * 0.99f,
            colors: new[]
            {
            new SKColor(255, 255, 255, 38),
            new SKColor(180, 220, 255, 18),
            SKColors.Transparent
            },
            colorPos: new[]
            {
            0.00f,
            0.35f,
            1.00f
            },
            mode: SKShaderTileMode.Clamp);

        using var highlightPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = highlightShader,
            BlendMode = SKBlendMode.Screen,
            FilterQuality = SKFilterQuality.High,

            // Tiny blur removes gradient harshness
            ImageFilter = SKImageFilter.CreateBlur(1.2f, 1.2f)
        };

        canvas.DrawCircle(cx, cy, radius, highlightPaint);


        // =========================================================
        // SECONDARY AMBIENT LIGHT
        // Very subtle atmospheric lift across upper hemisphere.
        // Prevents the center from feeling hollow or flat.
        // =========================================================

        using var ambientShader = SKShader.CreateLinearGradient(
            new SKPoint(cx, cy - radius),
            new SKPoint(cx, cy + radius),
            new[]
            {
            new SKColor(255, 255, 255, 14),
            SKColors.Transparent,
            new SKColor(0, 0, 0, 25),
            },
            new[]
            {
            0.0f,
            0.45f,
            1.0f
            },
            SKShaderTileMode.Clamp);

        using var ambientPaint = new SKPaint
        {
            IsAntialias = true,
            Shader = ambientShader,
            BlendMode = SKBlendMode.SoftLight,
        };

        canvas.DrawCircle(cx, cy, radius, ambientPaint);


        // =========================================================
        // FRESNEL RIM
        // Extremely subtle edge reflection.
        // If this becomes visibly "ring-like", reduce alpha.
        // =========================================================

        using var rimPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = radius * 0.008f,
            Color = new SKColor(255, 255, 255, 18),
            BlendMode = SKBlendMode.Screen
        };

        canvas.DrawCircle(cx, cy, radius - rimPaint.StrokeWidth, rimPaint);
    }

    public static void DrawRollScale(SKCanvas canvas, float cx, float cy, float R, float br)
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

            float lenFrac = major ? 0.58f : mid ? 0.34f : 0.18f;
            float thick   = major ? 2.2f  : mid ? 1.3f  : 0.85f;
            byte  alpha   = major ? (byte)205 : mid ? (byte)160 : (byte)95;

            float rad = (deg - 90f) * MathF.PI / 180f;
            float cos = MathF.Cos(rad);
            float sin = MathF.Sin(rad);

            float midR = br + bz * 0.5f;
            float half = bz * lenFrac * 0.5f;
            float r1 = midR - half;
            float r2 = midR + half;

            paint.StrokeWidth = thick;
            paint.Color = new SKColor(0xCC, 0xCC, 0xDC, alpha);
            canvas.DrawLine(cx + cos * r1, cy + sin * r1, cx + cos * r2, cy + sin * r2, paint);
        }
    }

    // The rotating orange bank pointer — drawn with rollDeg applied.
    public static void DrawRotatingPointer(SKCanvas canvas, float cx, float cy, float R, float br, float rollDeg)
    {
        float bz = R - br;
        canvas.Save();
        canvas.RotateDegrees(rollDeg, cx, cy);
        float rW = bz * 0.46f;
        float rH = bz * 0.50f;
        using var path = new SKPath();
        path.MoveTo(cx, cy - br + 1f);
        path.LineTo(cx - rW / 2, cy - br - rH);
        path.LineTo(cx + rW / 2, cy - br - rH);
        path.Close();
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0xFF, 0xA0, 0x00, 225) };
        canvas.DrawPath(path, fill);
        canvas.Restore();
    }

    // The fixed white reference indicator at the top of the bezel.
    public static void DrawFixedIndicator(SKCanvas canvas, float cx, float cy, float R, float br)
    {
        float bz = R - br;
        float fW = bz * 0.40f;
        float fOuter = cy - R + bz * 0.14f;
        float fInner = cy - br;
        using var path = new SKPath();
        path.MoveTo(cx, fInner);
        path.LineTo(cx - fW / 2, fOuter);
        path.LineTo(cx + fW / 2, fOuter);
        path.Close();
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0xFF, 0xFF, 0xFF, 230) };
        canvas.DrawPath(path, fill);
    }

    public static void DrawAircraftSymbol(SKCanvas canvas, float cx, float cy, float br)
    {
        float armLen  = br * 0.30f;
        float armGap  = br * 0.13f;
        float thick   = br * 0.030f;
        float ringR   = br * 0.072f;
        float dotR    = br * 0.044f;
        var   amber   = new SKColor(0xFF, 0x95, 0x00);

        using var paint = new SKPaint { IsAntialias = true, StrokeCap = SKStrokeCap.Round };

        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = thick;
        paint.Color = amber;
        canvas.DrawLine(cx - armGap - armLen, cy, cx - armGap, cy, paint);
        canvas.DrawLine(cx + armGap, cy, cx + armGap + armLen, cy, paint);

        paint.StrokeWidth = thick * 0.80f;
        canvas.DrawLine(cx, cy, cx, cy + dotR * 2.2f, paint);

        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = thick * 0.65f;
        canvas.DrawCircle(cx, cy, ringR, paint);

        paint.Style = SKPaintStyle.Fill;
        canvas.DrawCircle(cx, cy, dotR, paint);
    }

    public static void DrawBezelEdge(SKCanvas canvas, float cx, float cy, float R, float br)
    {
        using var shadow = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, Color = new SKColor(0, 0, 0, 210) };
        canvas.DrawCircle(cx, cy, br, shadow);

        using var accent = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(0xFF, 0x95, 0x00, 38) };
        canvas.DrawCircle(cx, cy, br - 2f, accent);

        using var outer = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(0x18, 0x18, 0x22, 120) };
        canvas.DrawCircle(cx, cy, R - 1f, outer);
    }

    // --- private helpers ---

    private static void DrawHorizonBall(SKCanvas canvas, float cx, float cy, float br, float rollDeg)
    {
        canvas.Save();
        using var clip = new SKPath();
        clip.AddCircle(cx, cy, br - 1.5f);
        canvas.ClipPath(clip);
        canvas.RotateDegrees(rollDeg, cx, cy);
        DrawHorizonContent(canvas, cx, cy, br);
        canvas.Restore();
    }

    private static void DrawBankPointer(SKCanvas canvas, float cx, float cy, float R, float br, float rollDeg)
    {
        DrawRotatingPointer(canvas, cx, cy, R, br, rollDeg);
        DrawFixedIndicator(canvas, cx, cy, R, br);
    }

    private static void DrawPitchLadder(SKCanvas canvas, float cx, float cy, float br)
    {
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
}
