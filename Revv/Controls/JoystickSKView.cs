using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv.Controls;

// Full 2-D virtual thumbstick.
// Drag the inner circle within the outer track; it springs back to center on release.
// ValueChanged fires (X, Y) normalized to -1..+1 on every movement and on spring-back.
public sealed class JoystickSKView : SKCanvasView
{
    public event EventHandler<(float X, float Y)>? ValueChanged;

    // ── Geometry constants (fractions of min(w,h) in canvas pixels) ───────────
    private const float TrackFraction = 0.44f;
    private const float ThumbFraction = 0.18f;
    private const float SpringDecay   = 0.78f;  // per 16 ms frame (~60 fps)

    // ── State ─────────────────────────────────────────────────────────────────
    private float _offsetX    = 0f;
    private float _offsetY    = 0f;
    private long  _touchId    = -1;

    private CancellationTokenSource? _springCts;

    // ── Constructor ───────────────────────────────────────────────────────────

    public JoystickSKView()
    {
        BackgroundColor   = Colors.Transparent;
        EnableTouchEvents = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private float TrackR  => MathF.Min(CanvasSize.Width, CanvasSize.Height) * TrackFraction;
    private float ThumbR  => MathF.Min(CanvasSize.Width, CanvasSize.Height) * ThumbFraction;
    private float CenterX => CanvasSize.Width  / 2f;
    private float CenterY => CanvasSize.Height / 2f;

    // ── Touch handling ────────────────────────────────────────────────────────

    protected override void OnTouch(SKTouchEventArgs e)
    {
        e.Handled = true;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed when _touchId < 0:
                _touchId = e.Id;
                StopSpring();
                ApplyTouch(e.Location.X - CenterX, e.Location.Y - CenterY);
                break;

            case SKTouchAction.Moved when e.Id == _touchId:
                ApplyTouch(e.Location.X - CenterX, e.Location.Y - CenterY);
                break;

            case SKTouchAction.Released when e.Id == _touchId:
            case SKTouchAction.Cancelled when e.Id == _touchId:
                _touchId = -1;
                StartSpring(TrackR);
                break;
        }
    }

    private void ApplyTouch(float dx, float dy)
    {
        float trackR = TrackR;
        float dist   = MathF.Sqrt((dx * dx) + (dy * dy));
        if (dist > trackR)
        {
            float scale = trackR / dist;
            dx *= scale;
            dy *= scale;
        }
        _offsetX = dx;
        _offsetY = dy;
        FireValue(trackR);
        InvalidateSurface();
    }

    private void FireValue(float trackR)
    {
        float nx = trackR > 0f ? Math.Clamp(_offsetX / trackR, -1f, 1f) : 0f;
        float ny = trackR > 0f ? Math.Clamp(_offsetY / trackR, -1f, 1f) : 0f;
        ValueChanged?.Invoke(this, (nx, ny));
    }

    // ── Spring return ─────────────────────────────────────────────────────────

    private void StartSpring(float trackR)
    {
        _springCts = new CancellationTokenSource();
        var cts = _springCts;
        _ = Task.Run(() => SpringLoopAsync(cts, trackR));
    }

    private void StopSpring()
    {
        _springCts?.Cancel();
        _springCts = null;
    }

    private async Task SpringLoopAsync(CancellationTokenSource cts, float trackR)
    {
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await Task.Delay(16, cts.Token);
                bool done = false;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _offsetX *= SpringDecay;
                    _offsetY *= SpringDecay;
                    done = MathF.Abs(_offsetX) < 0.4f && MathF.Abs(_offsetY) < 0.4f;
                    if (done) { _offsetX = 0f; _offsetY = 0f; }
                    FireValue(trackR);
                    InvalidateSurface();
                });
                if (done) break;
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── Paint ─────────────────────────────────────────────────────────────────

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        float cx     = e.Info.Width  / 2f;
        float cy     = e.Info.Height / 2f;
        float minDim = MathF.Min(e.Info.Width, e.Info.Height);
        float trackR = minDim * TrackFraction;
        float thumbR = minDim * ThumbFraction;

        DrawTrack(canvas, cx, cy, trackR);
        DrawThumb(canvas, cx + _offsetX, cy + _offsetY, thumbR, _offsetX, _offsetY, trackR);
    }

    // ── Track (outer ring) ────────────────────────────────────────────────────

    private static void DrawTrack(SKCanvas canvas, float cx, float cy, float r)
    {
        // Dark fill
        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
            Color       = new SKColor(0x1A, 0x1A, 0x1A, 210),
        };
        canvas.DrawCircle(cx, cy, r, fill);

        // Border
        using var border = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            Color       = new SKColor(0x2A, 0x2A, 0x2A),
        };
        canvas.DrawCircle(cx, cy, r, border);

        // Crosshair lines (very subtle)
        using var cross = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            Color       = new SKColor(0x33, 0x33, 0x33),
        };
        float arm = r * 0.72f;
        canvas.DrawLine(cx - arm, cy, cx + arm, cy, cross);
        canvas.DrawLine(cx, cy - arm, cx, cy + arm, cross);

        // Center dot
        using var dot = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Fill,
            Color       = new SKColor(0x2E, 0x2E, 0x2E),
        };
        canvas.DrawCircle(cx, cy, 3f, dot);
    }

    // ── Thumb (inner draggable circle) ────────────────────────────────────────

    private static void DrawThumb(SKCanvas canvas, float cx, float cy, float r,
                                  float offsetX, float offsetY, float trackR)
    {
        float magnitude = trackR > 0f
            ? MathF.Sqrt((offsetX * offsetX) + (offsetY * offsetY)) / trackR
            : 0f;
        byte accentAlpha = (byte)(120 + (magnitude * 135f));   // brighter red at full deflection

        // Drop shadow — depth increases with deflection
        float shadowBlur = r * (0.18f + (magnitude * 0.18f));
        using var shadow = new SKPaint
        {
            IsAntialias = true,
            MaskFilter  = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, shadowBlur),
            Color       = new SKColor(0, 0, 0, (byte)(100 + (magnitude * 100f))),
        };
        canvas.DrawCircle(cx + (r * 0.08f), cy + (r * 0.12f), r * 0.94f, shadow);

        // Body — dome gradient (dark gray)
        using var body = new SKPaint { IsAntialias = true };
        using var bodyShader = SKShader.CreateLinearGradient(
            new SKPoint(cx, cy - r),
            new SKPoint(cx, cy + r),
            [new SKColor(0x4A, 0x4A, 0x4A), new SKColor(0x1C, 0x1C, 0x1C)],
            null, SKShaderTileMode.Clamp);
        body.Shader = bodyShader;
        canvas.DrawCircle(cx, cy, r, body);

        // Red accent ring — glows brighter at full deflection
        using var accent = new SKPaint
        {
            IsAntialias = true,
            Style       = SKPaintStyle.Stroke,
            StrokeWidth = r * 0.11f,
            Color       = new SKColor(0xE8, 0x00, 0x1D, accentAlpha),
        };
        canvas.DrawCircle(cx, cy, r * 0.76f, accent);

        // Specular highlight (top-left dome reflection)
        using var hl = new SKPaint { IsAntialias = true };
        using var hlShader = SKShader.CreateRadialGradient(
            new SKPoint(cx - (r * 0.18f), cy - (r * 0.28f)), r * 0.52f,
            [new SKColor(0xFF, 0xFF, 0xFF, 80), new SKColor(0xFF, 0xFF, 0xFF, 0)],
            null, SKShaderTileMode.Clamp);
        hl.Shader = hlShader;
        canvas.DrawCircle(cx, cy, r, hl);
    }
}
