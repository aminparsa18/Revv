using SkiaSharp;

namespace Revv.Shared;

// Pure Skia drawing logic — no platform dependency.
// Call Draw() from any SkiaSharp host: SKCanvasView (MAUI) or SKControl (WinForms).
public static class AttitudeRenderer
{
    // Per-pixel sphere lighting: Lambert diffuse + Phong specular + Fresnel rim.
    // Gradients can't do this without producing circular artifacts; SKSL can.
    private const string SphereSksl = """
        uniform float2 center;
        uniform float  radius;

        half4 main(float2 p) {
            float2 d = p - center;
            if (dot(d, d) >= radius * radius) return half4(0, 0, 0, 0);

            float nx =  d.x / radius;
            float ny =  d.y / radius;
            float nz = sqrt(max(0.0, 1.0 - nx*nx - ny*ny));

            // Pre-normalised light: upper-left, elevated
            float3 L = float3(-0.4504, -0.6005, 0.6607);
            float3 N = float3(nx, ny, nz);

            float diff = max(dot(N, L), 0.0);

            // Wide soft specular — low exponent spreads it across the hemisphere
            float3 R   = 2.0 * diff * N - L;
            float spec = pow(max(R.z, 0.0), 6.0);

            // Fresnel: edge normals face away from viewer → less contribution
            float rim = pow(1.0 - nz, 3.0);

            float v = clamp(diff * 0.42 + spec * 0.28 - rim * 0.28, 0.0, 1.0);
            return half4(v, v, v, v * 0.70);
        }
        """;

    private static readonly SKRuntimeEffect? _sphereEffect =
        SKRuntimeEffect.CreateShader(SphereSksl, out _);

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
        using SKShader shader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), R * 1.07f,
            [
                SKColors.Transparent,
                SKColors.Transparent,
                new SKColor(0xE8, 0x00, 0x1D, 18),
                SKColors.Transparent,
            ],
            [0f, 0.80f, 0.93f, 1.0f],
            SKShaderTileMode.Clamp);
        using SKPaint paint = new() { IsAntialias = true, Shader = shader };
        canvas.DrawCircle(cx, cy, R * 1.07f, paint);
    }

    public static void DrawBezel(SKCanvas canvas, float cx, float cy, float R, float br)
    {
        using (SKPaint paint = new()
        { IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy - R), new SKPoint(cx, cy + R),
                [
                    new SKColor(0x32, 0x32, 0x3E),
                    new SKColor(0x22, 0x22, 0x2E),
                    new SKColor(0x16, 0x16, 0x1E),
                    new SKColor(0x10, 0x10, 0x18),
                    new SKColor(0x08, 0x07, 0x06),
                ],
                [0f, 0.18f, 0.50f, 0.80f, 1.0f],
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawCircle(cx, cy, R, paint);
        }

        using SKPaint rim = new()
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f, Color = new SKColor(0x08, 0x08, 0x10, 220),
        };
        canvas.DrawCircle(cx, cy, R - 1.25f, rim);

        using SKPaint innerShadow = new()
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
        using (SKPaint paint = new()
        { IsAntialias = true })
        {
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy - br), new SKPoint(cx, cy + br * 0.15f),
                [
                    new SKColor(0x00, 0x38, 0xB8),
                    new SKColor(0x00, 0x62, 0xE8),
                    new SKColor(0x00, 0x82, 0xFF),
                ],
                [0f, 0.52f, 1.0f],
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawRect(cx - br, cy - br, br * 2f, br * 2f, paint);
        }

        using (SKPaint paint = new()
        { IsAntialias = true })
        {
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(cx, cy), new SKPoint(cx, cy + br),
                [
                    new SKColor(0x0D, 0x18, 0x28),
                    new SKColor(0x06, 0x0C, 0x14),
                    new SKColor(0x02, 0x04, 0x08),
                ],
                [0f, 0.45f, 1.0f],
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawRect(cx - br, cy, br * 2f, br, paint);
        }

        DrawPitchLadder(canvas, cx, cy, br);

        using (SKPaint paint = new()
        { IsAntialias = true, Color = new SKColor(0xFF, 0x95, 0x00, 50) })
        {
            canvas.DrawRect(cx - br, cy - 7, br * 2f, 14, paint);
        }

        using (SKPaint paint = new()
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f, Color = new SKColor(0xFF, 0x95, 0x00),
        })
        {
            canvas.DrawLine(cx - br, cy, cx + br, cy, paint);
        }
    }

    public static void DrawSphereOverlay(SKCanvas canvas, float cx, float cy, float radius)
    {
        // Physics-based lighting via SKSL — no gradient circles, no rings
        if (_sphereEffect is not null)
        {
            SKRuntimeEffectUniforms uniforms = new(_sphereEffect);
            uniforms["center"] = new[] { cx, cy };
            uniforms["radius"] = radius;
            using SKShader shader = _sphereEffect.ToShader(uniforms);
            using SKPaint paint  = new() { IsAntialias = true, Shader = shader, BlendMode = SKBlendMode.Screen };
            canvas.DrawCircle(cx, cy, radius, paint);
        }

        // Rim vignette — curvature sends edge normals away from the viewer
        using SKShader vigShader = SKShader.CreateRadialGradient(
            new SKPoint(cx, cy), radius,
            [SKColors.Transparent, SKColors.Transparent, new(0, 0, 0, 50), new(0, 0, 0, 85)],
            [0f, 0.70f, 0.90f, 1.0f],
            SKShaderTileMode.Clamp);
        using SKPaint vigPaint = new() { IsAntialias = true, Shader = vigShader };
        canvas.DrawCircle(cx, cy, radius, vigPaint);
    }

    public static void DrawRollScale(SKCanvas canvas, float cx, float cy, float R, float br)
    {
        float bz = R - br;

        using SKPaint paint = new()
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
        using SKPath path = new();
        path.MoveTo(cx, cy - br + 1f);
        path.LineTo(cx - rW / 2, cy - br - rH);
        path.LineTo(cx + rW / 2, cy - br - rH);
        path.Close();
        using SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0xFF, 0xA0, 0x00, 225) };
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
        using SKPath path = new();
        path.MoveTo(cx, fInner);
        path.LineTo(cx - fW / 2, fOuter);
        path.LineTo(cx + fW / 2, fOuter);
        path.Close();
        using SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill, Color = new SKColor(0xFF, 0xFF, 0xFF, 230) };
        canvas.DrawPath(path, fill);
    }

    public static void DrawAircraftSymbol(SKCanvas canvas, float cx, float cy, float br)
    {
        float armLen  = br * 0.30f;
        float armGap  = br * 0.13f;
        float thick   = br * 0.030f;
        float ringR   = br * 0.072f;
        float dotR    = br * 0.044f;
        SKColor amber   = new(0xFF, 0x95, 0x00);

        using SKPaint paint = new() { IsAntialias = true, StrokeCap = SKStrokeCap.Round };

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
        using SKPaint shadow = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f, Color = new SKColor(0, 0, 0, 210) };
        canvas.DrawCircle(cx, cy, br, shadow);

        using SKPaint accent = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(0xFF, 0x95, 0x00, 38) };
        canvas.DrawCircle(cx, cy, br - 2f, accent);

        using SKPaint outer = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(0x18, 0x18, 0x22, 120) };
        canvas.DrawCircle(cx, cy, R - 1f, outer);
    }

    // --- private helpers ---

    private static void DrawHorizonBall(SKCanvas canvas, float cx, float cy, float br, float rollDeg)
    {
        canvas.Save();
        using SKPath clip = new();
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

        (int deg, float halfW)[] marks =
        [
            (-30, 0.46f), (-20, 0.34f), (-10, 0.27f), (-5, 0.15f),
            (5, 0.15f), (10, 0.27f), (20, 0.34f), (30, 0.46f),
        ];

        using SKPaint linePaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        using SKFont  textFont  = new(SKTypeface.Default, br * 0.092f);
        using SKPaint textPaint = new() { IsAntialias = true, Color = new SKColor(0xFF, 0xFF, 0xFF, 195) };

        foreach ((int deg, float wf) in marks)
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
                float tw = textFont.MeasureText(label, textPaint);
                float ty = y + textFont.Size * 0.36f;
                canvas.DrawText(label, cx - hw - tw - 5, ty, textFont, textPaint);
                canvas.DrawText(label, cx + hw + 5, ty, textFont, textPaint);
            }
        }
    }
}
