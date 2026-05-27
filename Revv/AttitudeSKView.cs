using Revv.Shared;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv;

// Caches the static layers of the attitude indicator so each frame only does
// cheap image blits + one small dynamic path draw. This avoids allocating
// ~30 Skia objects per frame, which was causing GC pauses that inflated the
// measured UDP round-trip time.
public sealed class AttitudeSKView : SKCanvasView
{
    private float _rollDeg;

    // Layer 0 — background (OuterGlow + Bezel): never changes
    // Layer 1 — horizon content: pre-rendered at roll=0, rotated at draw time
    // Layer 2 — mid overlay (SphereOverlay + RollScale): never changes
    // Layer 3 — top (FixedIndicator + AircraftSymbol + BezelEdge): never changes
    private SKImage? _bgImage, _horizonImage, _midImage, _topImage;
    private int _cacheW, _cacheH;

    public AttitudeSKView()
    {
        BackgroundColor   = Colors.Transparent;
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

        int w = e.Info.Width, h = e.Info.Height;
        if (w < 1 || h < 1) return;

        EnsureCaches(w, h);

        float cx = w / 2f, cy = h / 2f, S = MathF.Min(w, h);
        float R = S / 2f * 0.96f, bz = R * 0.145f, br = R - bz;

        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(0x0A, 0x0A, 0x0A));

        // Static background
        canvas.DrawImage(_bgImage!, 0, 0);

        // Dynamic: horizon ball — pre-rendered content rotated into place
        canvas.Save();
        using (var clip = new SKPath())
        {
            clip.AddCircle(cx, cy, br - 1.5f);
            canvas.ClipPath(clip);
        }
        canvas.RotateDegrees(_rollDeg, cx, cy);
        canvas.DrawImage(_horizonImage!, 0, 0);
        canvas.Restore();

        // Static mid overlay (sphere vignette + roll scale ticks)
        canvas.DrawImage(_midImage!, 0, 0);

        // Dynamic: rotating orange bank pointer
        AttitudeRenderer.DrawRotatingPointer(canvas, cx, cy, R, br, _rollDeg);

        // Static top (fixed reference indicator + aircraft symbol + bezel edge)
        canvas.DrawImage(_topImage!, 0, 0);
    }

    private void EnsureCaches(int w, int h)
    {
        if (w == _cacheW && h == _cacheH && _bgImage != null) return;

        _bgImage?.Dispose();
        _horizonImage?.Dispose();
        _midImage?.Dispose();
        _topImage?.Dispose();

        _cacheW = w;
        _cacheH = h;

        float cx = w / 2f, cy = h / 2f, S = MathF.Min(w, h);
        float R = S / 2f * 0.96f, bz = R * 0.145f, br = R - bz;
        var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);

        _bgImage = Render(info, c =>
        {
            AttitudeRenderer.DrawOuterGlow(c, cx, cy, R);
            AttitudeRenderer.DrawBezel(c, cx, cy, R, br);
        });

        _horizonImage = Render(info, c =>
            AttitudeRenderer.DrawHorizonContent(c, cx, cy, br));

        _midImage = Render(info, c =>
        {
            AttitudeRenderer.DrawSphereOverlay(c, cx, cy, br);
            AttitudeRenderer.DrawRollScale(c, cx, cy, R, br);
        });

        _topImage = Render(info, c =>
        {
            AttitudeRenderer.DrawFixedIndicator(c, cx, cy, R, br);
            AttitudeRenderer.DrawAircraftSymbol(c, cx, cy, br);
            AttitudeRenderer.DrawBezelEdge(c, cx, cy, R, br);
        });
    }

    private static SKImage Render(SKImageInfo info, Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        draw(surface.Canvas);
        return surface.Snapshot();
    }
}
