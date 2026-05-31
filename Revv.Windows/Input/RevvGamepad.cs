using System.Diagnostics;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Revv.Shared.Networking;

namespace Revv.Windows.Input;

public class RevvGamepad : IDisposable
{
    /// <summary>Deadband applied to the stick output. Values within ±Deadband are snapped to zero.</summary>
    public float Deadband { get; set; } = 0.02f;

    public bool IsConnected { get; private set; } = false;
    public float CurrentSteering { get; private set; } = 0f;
    public float CurrentThrottle { get; private set; } = 0f;
    public float CurrentBrake { get; private set; } = 0f;

    public event EventHandler? GamepadConnected;
    public event EventHandler? GamepadDisconnected;
    public event EventHandler<Exception>? ErrorOccurred;
    public event EventHandler<(byte Large, byte Small)>? RumbleReceived;

    private byte _lastLargeMotor = 0;
    private byte _lastSmallMotor = 0;

    private ViGEmClient? _client;
    private IXbox360Controller? _controller;
    private bool _disposed = false;

    // Latest values received from phone — written by network thread, read by render timer
    private volatile float  _targetSteering = 0f;
    private volatile float  _targetThrottle = 0f;
    private volatile float  _targetBrake    = 0f;
    private volatile ushort _targetButtons  = 0;

    // 200 Hz render loop: dedicated thread with sleep+spin for deterministic timing
    private Thread? _renderThread;
    private volatile bool _renderRunning;
    private long _prevTickTimestamp;

    // Spring-damper: ωₙ=√stiffness, ζ=damping/(2×ωₙ). At 1600/64: ωₙ=40 rad/s, ζ=0.8 → ~35ms tracking lag
    public float SteerStiffness { get; set; } = 1600f;
    public float SteerDamping   { get; set; } = 64f;
    private float _steeringVelocity = 0f;

    // Speed-sensitive steering: reduces output at high speed to prevent snap oversteer
    // scale = 1 / (1 + speedMs * SpeedSensitivity)
    // At 0.02: 100 km/h → 64%, 200 km/h → 47%
    public float SpeedSensitivity { get; set; } = 0.02f;
    private volatile float _speedMs = 0f;
    public void UpdateSpeed(float speedMs) => _speedMs = MathF.Max(0f, speedMs);

    // Last values actually submitted to ViGEm — skip report when unchanged
    private short  _lastSteerShort   = short.MinValue; // sentinel forces first submit
    private byte   _lastThrottleByte = 0;
    private byte   _lastBrakeByte    = 0;
    private ushort _lastButtons      = 0;

    public void Connect()
    {
        if (IsConnected)
        {
            return;
        }

        try
        {
            _client = new ViGEmClient();
            _controller = _client.CreateXbox360Controller();
            _controller.Connect();
            _controller.FeedbackReceived += OnFeedbackReceived;

            IsConnected = true;

            _prevTickTimestamp = Stopwatch.GetTimestamp();
            _lastSteerShort = short.MinValue; // force first submit after connect
            _renderRunning = true;
            _renderThread = new Thread(RenderLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
            _renderThread.Start();

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
        if (!IsConnected)
        {
            return;
        }

        _renderRunning = false;
        _renderThread?.Join(200);
        _renderThread = null;

        try
        {
            ZeroAllInputs();
            _controller?.FeedbackReceived -= OnFeedbackReceived;
            _lastLargeMotor = 0;
            _lastSmallMotor = 0;
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

    // Called by network thread — just updates the target, render loop applies it
    public void SetSteering(float normalizedValue)
    {
        if (!IsConnected)
        {
            return;
        }

        _targetSteering = Math.Clamp(normalizedValue, -1f, 1f);
    }

    public void SetThrottle(float normalizedValue)
    {
        if (!IsConnected)
        {
            return;
        }

        _targetThrottle = Math.Clamp(normalizedValue, 0f, 1f);
    }

    public void SetBrake(float normalizedValue)
    {
        if (!IsConnected)
        {
            return;
        }

        _targetBrake = Math.Clamp(normalizedValue, 0f, 1f);
    }

    public void SetButtons(ushort buttons)
    {
        if (!IsConnected)
        {
            return;
        }

        _targetButtons = buttons;
    }

    public void ZeroAllInputs()
    {
        _targetSteering = 0f;
        _targetThrottle = 0f;
        _targetBrake    = 0f;
        _targetButtons  = 0;
        CurrentSteering = 0f;
        CurrentThrottle = 0f;
        CurrentBrake    = 0f;
        _steeringVelocity = 0f;

        if (!IsConnected || _controller is null)
        {
            return;
        }

        _controller.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
        _controller.SetSliderValue(Xbox360Slider.RightTrigger, 0);
        _controller.SetSliderValue(Xbox360Slider.LeftTrigger, 0);
        _controller.SetButtonState(Xbox360Button.A,     false);
        _controller.SetButtonState(Xbox360Button.B,     false);
        _controller.SetButtonState(Xbox360Button.X,     false);
        _controller.SetButtonState(Xbox360Button.Y,     false);
        _controller.SetButtonState(Xbox360Button.Start, false);
        _controller.SubmitReport();
    }

    private void OnFeedbackReceived(object sender, Xbox360FeedbackReceivedEventArgs e)
    {
        if (e.LargeMotor == _lastLargeMotor && e.SmallMotor == _lastSmallMotor)
        {
            return;
        }

        _lastLargeMotor = e.LargeMotor;
        _lastSmallMotor = e.SmallMotor;
        RumbleReceived?.Invoke(this, (e.LargeMotor, e.SmallMotor));
    }

    private void RenderLoop()
    {
        long targetTicks = Stopwatch.Frequency / 200; // 5 ms per tick at 200 Hz
        Stopwatch sw = Stopwatch.StartNew();
        long nextTick = sw.ElapsedTicks;

        while (_renderRunning)
        {
            nextTick += targetTicks;
            OnRenderTick();

            // Sleep for most of the interval, spin-wait for the last sub-ms slice
            long remaining = nextTick - sw.ElapsedTicks;
            if (remaining > 0)
            {
                long sleepMs = (remaining * 1000 / Stopwatch.Frequency) - 1;
                if (sleepMs > 0)
                {
                    Thread.Sleep((int)sleepMs);
                }

                while (sw.ElapsedTicks < nextTick)
                {
                    Thread.SpinWait(1);
                }
            }
        }
    }

    private void OnRenderTick()
    {
        if (!IsConnected || _controller is null)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        float dt = (float)(now - _prevTickTimestamp) / Stopwatch.Frequency;
        _prevTickTimestamp = now;

        // Critically damped spring: quick response, no oscillation, weighted feel
        float accel = ((_targetSteering - CurrentSteering) * SteerStiffness)
                      - (_steeringVelocity * SteerDamping);
        _steeringVelocity += accel * dt;
        CurrentSteering = Math.Clamp(CurrentSteering + (_steeringVelocity * dt), -1f, 1f);

        CurrentThrottle = _targetThrottle;
        CurrentBrake = _targetBrake;

        float dynamicDeadband = Deadband / (1f + (MathF.Abs(_steeringVelocity) * 8f));
        float steerOut = MathF.Abs(CurrentSteering) < dynamicDeadband ? 0f : CurrentSteering;

        // Speed-sensitive scaling — 1.0 at rest, reduces as speed increases
        float speedScale = 1f / (1f + (_speedMs * SpeedSensitivity));
        steerOut *= speedScale;

        short  steerShort    = (short)(-Math.Clamp(steerOut, -1f, 1f) * short.MaxValue);
        byte   throttleByte  = (byte)(CurrentThrottle * 255f);
        byte   brakeByte     = (byte)(CurrentBrake * 255f);
        ushort buttons       = _targetButtons;

        if (steerShort == _lastSteerShort && throttleByte == _lastThrottleByte
            && brakeByte == _lastBrakeByte && buttons == _lastButtons)
        {
            return;
        }

        _lastSteerShort   = steerShort;
        _lastThrottleByte = throttleByte;
        _lastBrakeByte    = brakeByte;

        try
        {
            _controller.SetAxisValue(Xbox360Axis.LeftThumbX, steerShort);
            _controller.SetSliderValue(Xbox360Slider.RightTrigger, throttleByte);
            _controller.SetSliderValue(Xbox360Slider.LeftTrigger, brakeByte);

            if (buttons != _lastButtons)
            {
                _lastButtons = buttons;
                _controller.SetButtonState(Xbox360Button.A,     (buttons & (ushort)RevvButtonMask.A)     != 0);
                _controller.SetButtonState(Xbox360Button.B,     (buttons & (ushort)RevvButtonMask.B)     != 0);
                _controller.SetButtonState(Xbox360Button.X,     (buttons & (ushort)RevvButtonMask.X)     != 0);
                _controller.SetButtonState(Xbox360Button.Y,     (buttons & (ushort)RevvButtonMask.Y)     != 0);
                _controller.SetButtonState(Xbox360Button.Start, (buttons & (ushort)RevvButtonMask.Start) != 0);
            }

            _controller.SubmitReport();
        }
        catch { /* ViGEm gone — Disconnect() will be called by the session */ }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Disconnect();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
