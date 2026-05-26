using SkiaSharp;

namespace Revv.Shared;

// Waiting / discovery pulse animation — three concentric rings expanding outward.
// Call Draw() from any SkiaSharp host. Advance phase 0.0→1.0 externally (e.g. 0.008/frame at 60fps).
public static class PulseRenderer
{
    private static readonly SKColor AccentRed = new(0xE8, 0x00, 0x1D);

    public static void Draw(SKCanvas canvas, float W, float H, float phase)
    {
        canvas.Clear(new SKColor(0x0A, 0x0A, 0x0A));

        float cx   = W / 2f;
        float cy   = H / 2f;
        float maxR = MathF.Min(W, H) * 0.40f;

        // Three rings staggered 1/3 cycle apart
        DrawRing(canvas, cx, cy, phase,                       maxR,        2.5f, 210);
        DrawRing(canvas, cx, cy, (phase + 0.33f) % 1f, maxR * 0.82f, 1.8f, 170);
        DrawRing(canvas, cx, cy, (phase + 0.66f) % 1f, maxR * 0.64f, 1.2f, 130);

        // Center dot
        float dotR = MathF.Min(W, H) * 0.028f;
        using var dot = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = AccentRed };
        canvas.DrawCircle(cx, cy, dotR, dot);
    }

    private static void DrawRing(SKCanvas canvas, float cx, float cy,
                                  float phase, float maxR, float strokeW, byte maxAlpha)
    {
        float r = phase * maxR;
        if (r <= 0f) return;
        int a = (int)((1f - phase) * maxAlpha);
        if (a <= 0) return;

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = strokeW,
            Color       = AccentRed.WithAlpha((byte)a),
        };
        canvas.DrawCircle(cx, cy, r, paint);
    }
}
