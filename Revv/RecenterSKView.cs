using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv;

public sealed class RecenterSKView : SKCanvasView
{
    public event EventHandler? Clicked;

    private bool _pressed = false;

    public RecenterSKView()
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
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float w   = e.Info.Width;
        float h   = e.Info.Height;
        float cx  = w / 2f;
        float cy  = h / 2f;
        float pad = 5f;
        float outerR = MathF.Min(cx, cy) - pad;

        // ── Drop shadow ────────────────────────────────────────────────────────
        using (var shadowPaint = new SKPaint
        {
            IsAntialias = true,
            MaskFilter  = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _pressed ? 4f : 10f),
            Color       = new SKColor(180, 0, 10, _pressed ? (byte)90 : (byte)180),
        })
        {
            canvas.Save();
            canvas.Translate(0, _pressed ? 1f : 5f);
            canvas.DrawCircle(cx, cy, outerR, shadowPaint);
            canvas.Restore();
        }

        // ── Bezel shell ────────────────────────────────────────────────────────
        using (var bezelShader = SKShader.CreateLinearGradient(
            new SKPoint(cx, cy - outerR),
            new SKPoint(cx, cy + outerR),
            new[] { new SKColor(55, 0, 8), new SKColor(12, 0, 3) },
            SKShaderTileMode.Clamp))
        using (var bezelPaint = new SKPaint { IsAntialias = true, Shader = bezelShader })
            canvas.DrawCircle(cx, cy, outerR, bezelPaint);

        // Bezel rim highlight
        using (var rimPaint = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1.4f,
            Color       = new SKColor(255, 100, 100, _pressed ? (byte)35 : (byte)90),
        })
            canvas.DrawCircle(cx, cy, outerR - 0.7f, rimPaint);

        // ── Face ───────────────────────────────────────────────────────────────
        float faceInset = _pressed ? 4f : 3f;
        float faceR     = outerR - faceInset;

        SKColor hi, mid, lo;
        if (_pressed)
        {
            hi  = new SKColor(150, 0, 22);
            mid = new SKColor(105, 0, 15);
            lo  = new SKColor(65,  0,  9);
        }
        else
        {
            hi  = new SKColor(248, 35, 55);
            mid = new SKColor(200, 0,  28);
            lo  = new SKColor(130, 0,  18);
        }

        using (var faceShader = SKShader.CreateLinearGradient(
            new SKPoint(cx * 0.35f, cy - faceR),
            new SKPoint(cx * 1.65f, cy + faceR),
            new[] { hi, mid, lo },
            new[] { 0f, 0.42f, 1f },
            SKShaderTileMode.Clamp))
        using (var facePaint = new SKPaint { IsAntialias = true, Shader = faceShader })
            canvas.DrawCircle(cx, cy, faceR, facePaint);

        // ── Specular gloss ─────────────────────────────────────────────────────
        if (!_pressed)
        {
            using var specShader = SKShader.CreateRadialGradient(
                new SKPoint(cx * 0.55f, cy * 0.45f),
                faceR * 0.75f,
                new[] { new SKColor(255, 255, 255, (byte)60), new SKColor(255, 255, 255, 0) },
                SKShaderTileMode.Clamp);
            using var specPaint = new SKPaint { IsAntialias = true, Shader = specShader };
            canvas.DrawCircle(cx, cy, faceR, specPaint);
        }

        // ── Crosshair icon ─────────────────────────────────────────────────────
        float off  = _pressed ? 0.8f : 0f;
        float icx  = cx + off;
        float icy  = cy + off;
        float side = MathF.Min(w, h);

        float ringR   = side * 0.20f;
        float tickGap = side * 0.04f;
        float tickLen = side * 0.10f;
        float sw      = _pressed ? side * 0.038f : side * 0.048f;

        using var iconPaint = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = sw,
            StrokeCap   = SKStrokeCap.Round,
            Color       = new SKColor(255, 255, 255, _pressed ? (byte)200 : (byte)255),
        };

        canvas.DrawCircle(icx, icy, ringR, iconPaint);
        canvas.DrawLine(icx, icy - ringR - tickGap, icx, icy - ringR - tickGap - tickLen, iconPaint);
        canvas.DrawLine(icx, icy + ringR + tickGap, icx, icy + ringR + tickGap + tickLen, iconPaint);
        canvas.DrawLine(icx - ringR - tickGap, icy, icx - ringR - tickGap - tickLen, icy, iconPaint);
        canvas.DrawLine(icx + ringR + tickGap, icy, icx + ringR + tickGap + tickLen, icy, iconPaint);

        using var dotPaint = new SKPaint
        {
            IsAntialias = true,
            Color       = new SKColor(255, 255, 255, _pressed ? (byte)200 : (byte)255),
        };
        canvas.DrawCircle(icx, icy, sw * 0.85f, dotPaint);
    }
}
