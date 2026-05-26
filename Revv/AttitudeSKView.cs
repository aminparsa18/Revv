using Revv.Shared;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Revv;

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
        AttitudeRenderer.Draw(e.Surface.Canvas, e.Info.Width, e.Info.Height, _rollDeg);
    }
}
