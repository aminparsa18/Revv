using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv.Controls;

public enum PedalType { Brake, Throttle }

public sealed class PedalSKView : SKCanvasView
{
    private float _pressT;
    private CancellationTokenSource? _animCts;

    public PedalType Type
    {
        get;
        set
        {
            field = value;
            InvalidateSurface();
        }
    } = PedalType.Brake;

    public PedalSKView()
    {
        BackgroundColor   = Colors.Transparent;
        EnableTouchEvents = false;
        InputTransparent  = true;
    }

    public void SetPressed(bool pressed)
    {
        _animCts?.Cancel();
        _animCts = new CancellationTokenSource();
        _ = AnimateTo(pressed ? 1f : 0f, pressed ? 40 : 100, _animCts.Token);
    }

    private async Task AnimateTo(float target, int durationMs, CancellationToken ct)
    {
        float start = _pressT;
        const int frameMs = 16;
        int frames = Math.Max(1, durationMs / frameMs);
        for (int i = 1; i <= frames; i++)
        {
            try { await Task.Delay(frameMs, ct); }
            catch (OperationCanceledException) { return; }
            _pressT = start + ((target - start) * i / frames);
            MainThread.BeginInvokeOnMainThread(InvalidateSurface);
        }
        _pressT = target;
        MainThread.BeginInvokeOnMainThread(InvalidateSurface);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float W = e.Info.Width;
        float H = e.Info.Height;
        float t = _pressT;

        bool isBrake = Type == PedalType.Brake;
        SKColor accent = isBrake ? new SKColor(0xE8, 0x00, 0x1D) : new SKColor(0x00, 0xA8, 0xFF);

        const float margin = 8f;
        SKRect bounds = new(margin, margin, W - margin, H - margin);
        float minDim = MathF.Min(bounds.Width, bounds.Height);

        using SKPath outer = MakePedalPath(bounds, 0f);
        using SKPath mid   = MakePedalPath(bounds, minDim * 0.085f);
        using SKPath inner = MakePedalPath(bounds, minDim * 0.170f);

        // ── Press skew transform ─────────────────────────────────────────────
        // Scale down from center + slight parallelogram lean — reads as physical push
        canvas.Save();
        if (t > 0.001f)
        {
            float scale = 1f - (t * 0.055f);   // shrink ~5.5% at full press
            float skewX = t * 0.07f;          // horizontal lean (~4°)

            canvas.Translate(W / 2f, H / 2f);
            canvas.Scale(scale);
            canvas.Skew(skewX, 0f);
            canvas.Translate(-W / 2f, -H / 2f);
        }

        DrawShadow(canvas, outer, t, accent);
        DrawBody(canvas, outer, mid, inner);
        DrawChevrons(canvas, inner, t, accent);
        DrawLED(canvas, bounds, t, accent, ledOnRight: isBrake);
        DrawSpecular(canvas, outer);

        canvas.Restore();
    }

    // ── Shape builder ────────────────────────────────────────────────────────────

    private static SKPath MakePedalPath(SKRect b, float inset)
    {
        float x0 = b.Left  + inset;
        float y0 = b.Top   + inset;
        float x1 = b.Right - inset;
        float y1 = b.Bottom - inset;
        float ch = MathF.Min(x1 - x0, y1 - y0) * 0.128f;

        SKPath p = new();
        p.MoveTo(x0 + ch, y0);
        p.LineTo(x1 - ch, y0);
        p.LineTo(x1,      y0 + ch);
        p.LineTo(x1,      y1 - ch);
        p.LineTo(x1 - ch, y1);
        p.LineTo(x0 + ch, y1);
        p.LineTo(x0,      y1 - ch);
        p.LineTo(x0,      y0 + ch);
        p.Close();
        return p;
    }

    // ── Shadow layer ─────────────────────────────────────────────────────────────

    private static void DrawShadow(SKCanvas canvas, SKPath outer, float t, SKColor accent)
    {
        // Drop shadow — simple offset fill, no blur to prevent Android bleed
        using SKPaint shadowPaint = new()
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
            Color       = new SKColor(0, 0, 0, (byte)(120 + (t * 60))),
        };
        canvas.Save();
        canvas.Translate(2f, 5f);
        canvas.DrawPath(outer, shadowPaint);
        canvas.Restore();

        // Ambient press glow — radial gradient clipped strictly to the path
        if (t > 0.01f)
        {
            outer.GetBounds(out SKRect b);
            canvas.Save();
            canvas.ClipPath(outer);
            using SKPaint glowPaint = new() { IsAntialias = true };
            using SKShader sh = SKShader.CreateRadialGradient(
                new SKPoint(b.MidX, b.MidY),
                MathF.Max(b.Width, b.Height) * 0.65f,
                [
                    new SKColor(accent.Red, accent.Green, accent.Blue, (byte)(80 * t)),
                    new SKColor(accent.Red, accent.Green, accent.Blue, 0),
                ],
                null, SKShaderTileMode.Clamp);
            glowPaint.Shader = sh;
            canvas.DrawRect(b, glowPaint);
            canvas.Restore();
        }
    }

    // ── Body: outer shell → mid shell → inner face ───────────────────────────────

    private static void DrawBody(SKCanvas canvas, SKPath outer, SKPath mid, SKPath inner)
    {
        outer.GetBounds(out SKRect ob);
        mid.GetBounds(out SKRect mb);
        inner.GetBounds(out SKRect ib);

        // Outer shell fill
        using (SKPaint paint = new()
        { IsAntialias = true })
        {
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(ob.Left, ob.Top),
                new SKPoint(ob.Right, ob.Bottom),
                [new SKColor(0x2C, 0x2C, 0x2C), new SKColor(0x16, 0x16, 0x16), new SKColor(0x07, 0x07, 0x07)],
                [0f, 0.45f, 1f],
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawPath(outer, paint);
        }
        // Outer chrome rim
        using (SKPaint rim = new()
        { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.8f })
        {
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(ob.Left, ob.Top), new SKPoint(ob.Right, ob.Bottom),
                [new SKColor(0xFF, 0xFF, 0xFF, 55), new SKColor(0xFF, 0xFF, 0xFF, 10)],
                null, SKShaderTileMode.Clamp);
            rim.Shader = sh;
            canvas.DrawPath(outer, rim);
        }
        // Junction shadow: outer → mid
        using (SKPaint js = new()
        { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5.5f, Color = new SKColor(0, 0, 0, 210) })
        {
            canvas.DrawPath(mid, js);
        }

        // Mid shell fill
        using (SKPaint paint = new()
        { IsAntialias = true })
        {
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(mb.Left, mb.Top),
                new SKPoint(mb.Right, mb.Bottom),
                [new SKColor(0x24, 0x24, 0x24), new SKColor(0x12, 0x12, 0x12), new SKColor(0x06, 0x06, 0x06)],
                [0f, 0.45f, 1f],
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawPath(mid, paint);
        }
        // Mid chrome rim
        using (SKPaint rim = new()
        { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f })
        {
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(mb.Left, mb.Top), new SKPoint(mb.Right, mb.Bottom),
                [new SKColor(0xFF, 0xFF, 0xFF, 44), new SKColor(0xFF, 0xFF, 0xFF, 8)],
                null, SKShaderTileMode.Clamp);
            rim.Shader = sh;
            canvas.DrawPath(mid, rim);
        }
        // Junction shadow: mid → inner
        using (SKPaint js = new()
        { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4.5f, Color = new SKColor(0, 0, 0, 195) })
        {
            canvas.DrawPath(inner, js);
        }

        // Inner face fill
        using (SKPaint paint = new()
        { IsAntialias = true })
        {
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(ib.Left, ib.Top),
                new SKPoint(ib.Right, ib.Bottom),
                [new SKColor(0x1C, 0x1C, 0x1C), new SKColor(0x0E, 0x0E, 0x0E), new SKColor(0x06, 0x06, 0x06)],
                [0f, 0.45f, 1f],
                SKShaderTileMode.Clamp);
            paint.Shader = sh;
            canvas.DrawPath(inner, paint);
        }
        // Inner chrome rim (very subtle)
        using SKPaint innerRim = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, Color = new SKColor(0xFF, 0xFF, 0xFF, 18) };
        canvas.DrawPath(inner, innerRim);
    }

    // ── Chevron arrows (open ∧, pointing up) ────────────────────────────────────

    private static void DrawChevrons(SKCanvas canvas, SKPath innerPath, float t, SKColor accent)
    {
        innerPath.GetBounds(out SKRect b);
        float cx = b.MidX;

        const int count = 5;
        float hw       = b.Width  * 0.44f;   // half-width of each arm end
        float spacing  = b.Height / (count + 1f);
        float chevH    = spacing  * 0.48f;   // vertical rise from baseline to peak
        float armThick = spacing  * 0.30f;   // stroke width of each arm
        float hlOffset = armThick * 0.44f;   // shift for surface highlight / shadow

        // Lay chevrons top→bottom; each baseline is where the two arms end
        float firstBase = b.Top + spacing;

        canvas.Save();
        canvas.ClipPath(innerPath);

        for (int i = 0; i < count; i++)
        {
            float baseY = firstBase + (i * spacing);
            float peakY = baseY - chevH;     // peak is above the baseline → points UP

            // Open ∧ path (not closed — true chevron, not a triangle)
            using SKPath chevPath = new();
            chevPath.MoveTo(cx - hw, baseY);
            chevPath.LineTo(cx,      peakY);
            chevPath.LineTo(cx + hw, baseY);

            // Accent blend: top chevrons get more accent when pressed
            float chevT = t * (0.35f + (0.65f * ((float)(count - 1 - i) / (count - 1))));
            byte  r  = (byte)(0x20 + ((accent.Red   - 0x20) * chevT * 0.72f));
            byte  g  = (byte)(0x20 + ((accent.Green - 0x20) * chevT * 0.72f));
            byte  bv = (byte)(0x20 + ((accent.Blue  - 0x20) * chevT * 0.72f));

            // ① Under-shadow — gives each arm a cast shadow below it
            using SKPaint shadowPaint = new()
            {
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = armThick,
                StrokeJoin  = SKStrokeJoin.Miter,
                StrokeMiter = 12f,
                Color       = new SKColor(0, 0, 0, 180),
            };
            canvas.Save();
            canvas.Translate(0, hlOffset);
            canvas.DrawPath(chevPath, shadowPaint);
            canvas.Restore();

            // ② Main arm body
            using SKPaint bodyPaint = new()
            {
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = armThick,
                StrokeJoin  = SKStrokeJoin.Miter,
                StrokeMiter = 12f,
                Color       = new SKColor(r, g, bv),
            };
            canvas.DrawPath(chevPath, bodyPaint);

            // ③ Upper-surface highlight — simulates light hitting the raised top face
            byte hlAlpha = (byte)(75 + (t * 40));
            using SKPaint hlPaint = new()
            {
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = 1.6f,
                StrokeJoin  = SKStrokeJoin.Miter,
                StrokeMiter = 12f,
                Color       = new SKColor(0x70, 0x72, 0x78, hlAlpha),
            };
            canvas.Save();
            canvas.Translate(0, -hlOffset);
            canvas.DrawPath(chevPath, hlPaint);
            canvas.Restore();
        }

        canvas.Restore();
    }

    // ── LED channel ──────────────────────────────────────────────────────────────

    private static void DrawLED(SKCanvas canvas, SKRect bounds, float t, SKColor accent, bool ledOnRight)
    {
        const float stripW = 4f;
        const float inset  = 2f;
        float x    = ledOnRight ? bounds.Right - inset - stripW : bounds.Left + inset;
        float yTop = bounds.Top    + (bounds.Height * 0.10f);
        float yBot = bounds.Bottom - (bounds.Height * 0.10f);
        float h    = yBot - yTop;

        // Always-on backing
        using SKPaint backPaint = new()
        {
            IsAntialias = true,
            Color = new SKColor(accent.Red, accent.Green, accent.Blue, 38),
        };
        canvas.DrawRect(x, yTop, stripW, h, backPaint);

        if (t <= 0.01f)
        {
            return;
        }

        // Active fill
        using (SKPaint ledPaint = new()
        { IsAntialias = true })
        {
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(x, yTop), new SKPoint(x, yBot),
                [
                    new SKColor(accent.Red, accent.Green, accent.Blue, (byte)(215 * t)),
                    new SKColor(accent.Red, accent.Green, accent.Blue, (byte)(195 * t)),
                    new SKColor(accent.Red, accent.Green, accent.Blue, (byte)(215 * t)),
                ],
                [0f, 0.5f, 1f], SKShaderTileMode.Clamp);
            ledPaint.Shader = sh;
            canvas.DrawRect(x, yTop, stripW, h, ledPaint);
        }

        // Glow — wider gradient fill, no MaskFilter to prevent bleed
        using SKPaint glowPaint = new() { IsAntialias = true };
        using SKShader glowSh = SKShader.CreateLinearGradient(
            new SKPoint(x - 8f, 0), new SKPoint(x + stripW + 8f, 0),
            [
                SKColors.Transparent,
                new SKColor(accent.Red, accent.Green, accent.Blue, (byte)(100 * t)),
                new SKColor(accent.Red, accent.Green, accent.Blue, (byte)(100 * t)),
                SKColors.Transparent,
            ],
            [0f, 0.3f, 0.7f, 1f],
            SKShaderTileMode.Clamp);
        glowPaint.Shader = glowSh;
        canvas.DrawRect(x - 8f, yTop, stripW + 16f, h, glowPaint);
    }

    // ── Specular reflection ──────────────────────────────────────────────────────

    private static void DrawSpecular(SKCanvas canvas, SKPath outer)
    {
        outer.GetBounds(out SKRect b);

        using SKPaint paint = new() { IsAntialias = true };
        using SKShader sh = SKShader.CreateRadialGradient(
            new SKPoint(b.Left + (b.Width * 0.18f), b.Top + (b.Height * 0.08f)),
            b.Width * 0.55f,
            [new SKColor(0xFF, 0xFF, 0xFF, 14), SKColors.Transparent],
            null, SKShaderTileMode.Clamp);
        paint.Shader = sh;
        canvas.DrawPath(outer, paint);
    }
}
