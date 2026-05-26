using Revv.Shared;
using SkiaSharp.Views.Desktop;

namespace Revv.Windows;

public sealed class AttitudeSKControl : SKControl
{
    private float _rollDeg;

    public void Update(float rollDeg)
    {
        _rollDeg = rollDeg;
        Invalidate();
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        AttitudeRenderer.Draw(e.Surface.Canvas, e.Info.Width, e.Info.Height, _rollDeg);
    }
}
