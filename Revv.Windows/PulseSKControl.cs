using Revv.Shared;
using SkiaSharp.Views.Desktop;

namespace Revv.Windows;

public sealed class PulseSKControl : SKControl
{
    private float _phase;
    private readonly System.Windows.Forms.Timer _timer;

    public PulseSKControl()
    {
        _timer = new System.Windows.Forms.Timer { Interval = 16 }; // ~60 fps
        _timer.Tick += (_, _) =>
        {
            _phase = (_phase + 0.008f) % 1f;
            Invalidate();
        };
    }

    public void StartPulse()
    {
        _phase = 0f;
        _timer.Start();
    }

    public void StopPulse() => _timer.Stop();

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);
        PulseRenderer.Draw(e.Surface.Canvas, e.Info.Width, e.Info.Height, _phase);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
