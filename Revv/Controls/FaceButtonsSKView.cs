using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv.Controls;

public enum FaceButton { A, B, X, Y }

// Xbox-style face button cluster: Y (top) · X (left) · B (right) · A (bottom).
// Each button is tappable; ButtonPressed / ButtonReleased fire on finger down / up.
// Works inside any layout — set WidthRequest/HeightRequest to size it.
public sealed class FaceButtonsSKView : SKCanvasView
{
    // ── Events ───────────────────────────────────────────────────────────────

    public event EventHandler<FaceButton>? ButtonPressed;
    public event EventHandler<FaceButton>? ButtonReleased;

    // ── Button definitions ────────────────────────────────────────────────────

    private static readonly (FaceButton btn, string label, SKColor color, float nx, float ny)[] ButtonDefs =
    [
        (FaceButton.Y, "Y", new SKColor(0xF0, 0xBB, 0x00),  0f, -1f),  // top    — gold
        (FaceButton.X, "X", new SKColor(0x18, 0x78, 0xE8), -1f,  0f),  // left   — blue
        (FaceButton.B, "B", new SKColor(0xE8, 0x00, 0x1D),  1f,  0f),  // right  — red
        (FaceButton.A, "A", new SKColor(0x00, 0xC0, 0x50),  0f,  1f),  // bottom — green
    ];

    // ── Touch state ───────────────────────────────────────────────────────────

    private readonly HashSet<FaceButton>          _held     = [];
    private readonly Dictionary<long, FaceButton> _touchMap = [];

    // ── Constructor ───────────────────────────────────────────────────────────

    public FaceButtonsSKView()
    {
        BackgroundColor   = Colors.Transparent;
        EnableTouchEvents = true;
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private static (float btnR, float spacing, float cx, float cy) Layout(SKImageInfo info)
    {
        float side    = MathF.Min(info.Width, info.Height);
        float btnR    = side * 0.18f;
        float spacing = side * 0.31f;
        return (btnR, spacing, info.Width / 2f, info.Height / 2f);
    }

    private static SKPoint BtnCenter(float nx, float ny, float spacing, float cx, float cy)
        => new(cx + (nx * spacing), cy + (ny * spacing));

    // ── Hit test ──────────────────────────────────────────────────────────────

    private FaceButton? HitTest(float x, float y, SKImageInfo info)
    {
        (float btnR, float spacing, float cx, float cy) = Layout(info);
        float hitR = btnR * 1.25f;
        foreach ((FaceButton btn, string _, SKColor _, float nx, float ny) in ButtonDefs)
        {
            SKPoint c = BtnCenter(nx, ny, spacing, cx, cy);
            float dx = x - c.X, dy = y - c.Y;
            if ((dx * dx) + (dy * dy) <= hitR * hitR)
            {
                return btn;
            }
        }
        return null;
    }

    // ── Touch handling ────────────────────────────────────────────────────────

    protected override void OnTouch(SKTouchEventArgs e)
    {
        e.Handled = true;
        SKImageInfo info  = new((int)CanvasSize.Width, (int)CanvasSize.Height);

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
            {
                    FaceButton? hit = HitTest(e.Location.X, e.Location.Y, info);
                if (hit is FaceButton btn)
                {
                    _touchMap[e.Id] = btn;
                    if (_held.Add(btn))
                    {
                        InvalidateSurface();
                        ButtonPressed?.Invoke(this, btn);
                    }
                }
                break;
            }
            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
            {
                if (_touchMap.Remove(e.Id, out FaceButton btn))
                {
                    bool stillHeld = _touchMap.Values.Any(b => b == btn);
                    if (!stillHeld && _held.Remove(btn))
                    {
                        InvalidateSurface();
                        ButtonReleased?.Invoke(this, btn);
                    }
                }
                break;
            }
        }
    }

    // ── Paint ─────────────────────────────────────────────────────────────────

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        (float btnR, float spacing, float cx, float cy) = Layout(e.Info);

        foreach ((FaceButton btn, string? label, SKColor color, float nx, float ny) in ButtonDefs)
        {
            DrawButton(canvas, BtnCenter(nx, ny, spacing, cx, cy), btnR, label, color, _held.Contains(btn));
        }
    }

    // ── 3-D button renderer ───────────────────────────────────────────────────

    private static void DrawButton(SKCanvas canvas, SKPoint center, float r,
                                   string label, SKColor color, bool pressed)
    {
        float faceR   = r * 0.76f;
        float pressedSinkX = r * 0.02f;
        float pressedSinkY = r * 0.04f;
        SKPoint faceCenter   = pressed
            ? new SKPoint(center.X + pressedSinkX, center.Y + pressedSinkY)
            : center;

        // ── Drop shadow (button raised above surface) ─────────────────────────
        {
            float blur   = pressed ? r * 0.08f  : r * 0.20f;
            float offset = pressed ? r * 0.03f  : r * 0.16f;
            byte  alpha  = pressed ? (byte)50   : (byte)170;
            using SKPaint p = new()
            {
                IsAntialias = true,
                MaskFilter  = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, blur),
                Color       = new SKColor(0, 0, 0, alpha),
            };
            canvas.DrawCircle(center.X + offset, center.Y + offset, r * 0.98f, p);
        }

        // ── Bezel (dark mount ring) ───────────────────────────────────────────
        {
            using SKPaint p = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(center.X, center.Y - r),
                new SKPoint(center.X, center.Y + r),
                [new(0x40, 0x40, 0x40), new(0x0A, 0x0A, 0x0A)],
                null, SKShaderTileMode.Clamp);
            p.Shader = sh;
            canvas.DrawCircle(center, r, p);
        }

        // Thin highlight arc on top of bezel (light grazes the rim edge)
        {
            float inset = r * 0.08f;
            SKRect rect  = new(center.X - r + inset, center.Y - r + inset,
                                     center.X + r - inset, center.Y + r - inset);
            using SKPaint p = new()
            {
                IsAntialias = true,
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = r * 0.055f,
                Color       = new SKColor(0x70, 0x70, 0x70, 130),
            };
            canvas.DrawArc(rect, 210f, 120f, false, p);   // top arc ~11 o'clock → 1 o'clock
        }

        // ── Button face with 3-D dome gradient ───────────────────────────────
        {
            SKColor hi  = Lighten(color, pressed ? 0.06f : 0.48f);
            SKColor mid = pressed ? Darken(color, 0.10f) : color;
            SKColor lo  = Darken(color, pressed ? 0.30f : 0.50f);

            using SKPaint p = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
            using SKShader sh = SKShader.CreateLinearGradient(
                new SKPoint(faceCenter.X, faceCenter.Y - faceR),
                new SKPoint(faceCenter.X, faceCenter.Y + faceR),
                [hi, mid, lo],
                [0f, 0.4f, 1f],
                SKShaderTileMode.Clamp);
            p.Shader = sh;
            canvas.DrawCircle(faceCenter, faceR, p);
        }

        // ── Specular highlight (convex dome reflection, top-left) ─────────────
        if (!pressed)
        {
            SKPoint hlC = new(faceCenter.X - (faceR * 0.13f), faceCenter.Y - (faceR * 0.30f));
            float hlR = faceR * 0.50f;
            using SKPaint p = new() { IsAntialias = true };
            using SKShader sh = SKShader.CreateRadialGradient(
                hlC, hlR,
                [new(0xFF, 0xFF, 0xFF, 105), new(0xFF, 0xFF, 0xFF, 0)],
                null, SKShaderTileMode.Clamp);
            p.Shader = sh;
            canvas.DrawCircle(hlC, hlR, p);
        }
        else
        {
            // Pressed: colored rim glow inside the bezel well
            using SKPaint p = new() { IsAntialias = true };
            using SKShader sh = SKShader.CreateRadialGradient(
                faceCenter, r,
                [
                    new(0, 0, 0, 0),
                    new(color.Red, color.Green, color.Blue, 70),
                ],
                [0.65f, 1f],
                SKShaderTileMode.Clamp);
            p.Shader = sh;
            canvas.DrawCircle(center, r, p);
        }

        // ── Label ─────────────────────────────────────────────────────────────
        float labelShift = pressed ? r * 0.04f : 0f;
        using SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
        using SKFont font = new(typeface, faceR * 0.90f);

        float advance = font.MeasureText(label, out SKRect b);
        float halfW   = advance / 2f;

        // Text drop shadow (unpressed only — depth illusion)
        if (!pressed)
        {
            using SKPaint shadow = new()
            {
                IsAntialias = true,
                Color       = new SKColor(0, 0, 0, 90),
                MaskFilter  = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 1.8f),
            };
            canvas.DrawText(label, faceCenter.X + 1.2f - halfW, faceCenter.Y - b.MidY + 1.8f, font, shadow);
        }

        // Main label
        using SKPaint text = new()
        {
            IsAntialias = true,
            Color       = pressed ? new SKColor(0xFF, 0xFF, 0xFF, 210) : SKColors.White,
        };
        canvas.DrawText(label, faceCenter.X - halfW, faceCenter.Y - b.MidY + labelShift, font, text);
    }

    // ── Color helpers ─────────────────────────────────────────────────────────

    private static SKColor Lighten(SKColor c, float t) => new(
        (byte)(c.Red   + ((255 - c.Red)   * t)),
        (byte)(c.Green + ((255 - c.Green) * t)),
        (byte)(c.Blue  + ((255 - c.Blue)  * t)));

    private static SKColor Darken(SKColor c, float t) => new(
        (byte)(c.Red   * (1f - t)),
        (byte)(c.Green * (1f - t)),
        (byte)(c.Blue  * (1f - t)));
}
