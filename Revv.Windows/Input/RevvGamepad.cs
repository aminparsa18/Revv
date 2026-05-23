using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Revv.Windows.Input;

public class RevvGamepad : IDisposable
{
    /// <summary>
    /// Deadband applied to the stick output.
    /// Values within ±Deadband of zero are snapped to zero. Range: 0.0 – 0.1.
    /// </summary>
    public float Deadband { get; set; } = 0.02f;

    /// <summary>
    /// Exponential moving average smoothing factor.
    /// 1.0 = raw (no smoothing), 0.5 = heavy smoothing. Range: 0.5 – 1.0.
    /// </summary>
    public float Smoothing { get; set; } = 0.85f;

    public bool IsConnected { get; private set; } = false;
    public float CurrentSteering { get; private set; } = 0f;
    public float CurrentThrottle { get; private set; } = 0f;
    public float CurrentBrake { get; private set; } = 0f;

    public event EventHandler? GamepadConnected;
    public event EventHandler? GamepadDisconnected;
    public event EventHandler<Exception>? ErrorOccurred;

    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool _disposed = false;

    public void Connect()
    {
        if (IsConnected) return;

        try
        {
            _client = new ViGEmClient();
            _controller = _client.CreateXbox360Controller();
            _controller.Connect();

            IsConnected = true;
            GamepadConnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
            throw;
        }
    }

    public void Disconnect()
    {
        if (!IsConnected) return;

        try
        {
            ZeroAllInputs();
            _controller?.Disconnect();
            _client?.Dispose();

            IsConnected = false;
            GamepadDisconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
        finally
        {
            _controller = null;
            _client = null;
        }
    }

    public void SetSteering(float normalizedValue)
    {
        if (!IsConnected || _controller is null) return;

        CurrentSteering = (normalizedValue * Smoothing) + (CurrentSteering * (1f - Smoothing));
        float output = MathF.Abs(CurrentSteering) < Deadband ? 0f : CurrentSteering;
        short axisValue = (short)(Math.Clamp(output, -1f, 1f) * short.MaxValue);

        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, axisValue);
        _controller.SubmitReport();
    }

    public void SetThrottle(float normalizedValue)
    {
        if (!IsConnected || _controller is null) return;

        CurrentThrottle = Math.Clamp(normalizedValue, 0f, 1f);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(CurrentThrottle * 255f));
        _controller.SubmitReport();
    }

    public void SetBrake(float normalizedValue)
    {
        if (!IsConnected || _controller is null) return;

        CurrentBrake = Math.Clamp(normalizedValue, 0f, 1f);
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(CurrentBrake * 255f));
        _controller.SubmitReport();
    }

    public void ZeroAllInputs()
    {
        if (!IsConnected || _controller is null) return;

        CurrentSteering = 0f;
        CurrentThrottle = 0f;
        CurrentBrake = 0f;

        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
        _controller.SubmitReport();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
