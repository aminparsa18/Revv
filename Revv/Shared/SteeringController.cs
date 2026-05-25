using System.Diagnostics;

namespace Revv.Shared;

/// <summary>
/// Complementary filter steering controller.
/// Gyroscope: fast, low-latency response.
/// Accelerometer: long-term drift correction via atan2(X,Y).
/// Both measure the same physical angle (Z-axis rotation held portrait toward chest).
/// </summary>
public class SteeringController
{
    // -------------------------------------------------------------------------
    // User-configurable settings
    // -------------------------------------------------------------------------

    /// <summary>Multiplier on the output. Higher = full lock with less physical rotation. Default: 1.4.</summary>
    public float Sensitivity { get; set; } = 1.4f;

    /// <summary>Physical rotation angle (degrees) that maps to full lock. Default: 45°.</summary>
    public float RangeDegrees { get; set; } = 45f;

    /// <summary>Output dead zone in degrees around center. Default: 0.5°.</summary>
    public float DeadZone { get; set; } = 0.5f;

    /// <summary>Expo curve exponent applied before tanh. 1.0 = linear. Higher = more center precision. Default: 1.2.</summary>
    public float SteeringExpo { get; set; } = 1.2f;

    /// <summary>tanh gain. Controls saturation softness — tanh(curved * gain). 2.5 → 99% output at full lock, soft rolloff beyond. Default: 2.5.</summary>
    public float SteeringGain { get; set; } = 2.5f;

    /// <summary>Strength of the self-centering spring. 0 = off. Default: 0.02.</summary>
    public float CenterAssist { get; set; } = 0.02f;

    /// <summary>When true, RangeDegrees auto-calibrates to the user's actual rotation extremes. Resets on Recenter().</summary>
    public bool AutoCalibrate { get; set; } = false;

    // -------------------------------------------------------------------------
    // State (read-only diagnostics)
    // -------------------------------------------------------------------------

    public float CurrentAngleDegrees { get; private set; } = 0f;
    public float CurrentValue { get; private set; } = 0f;
    public bool IsRunning { get; private set; } = false;

    // -------------------------------------------------------------------------
    // Filter state
    // -------------------------------------------------------------------------

    private float _gyroAngle = 0f;      // integrated + fused angle (absolute degrees)
    private float _accelAngle = 0f;     // absolute reference from gravity
    private float _centerAngle = 0f;    // calibrated neutral position
    private float _smoothedDegrees = 0f;
    private bool _calibrated = false;
    private long _prevGyroTimestamp;
    private float _smoothedDt = 0.01f;  // EMA of inter-callback interval, guards against jitter
    private bool _isCentered = true;    // hysteresis state — true = output locked to zero
    private float _sessionLeftMax  = 0f; // most negative steeringDegrees seen this session
    private float _sessionRightMax = 0f; // most positive steeringDegrees seen this session
    private int   _calibrationSamples = 0;

    private const float RadToDeg = 180f / MathF.PI;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    public event EventHandler<float>? SteeringChanged;
    public event EventHandler? Recentered;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    public void Start(SensorSpeed sensorSpeed = SensorSpeed.Fastest)
    {
        if (IsRunning) return;

        if (!Gyroscope.Default.IsSupported)
            throw new NotSupportedException("This device does not have a gyroscope.");
        if (!Accelerometer.Default.IsSupported)
            throw new NotSupportedException("This device does not have an accelerometer.");

        _calibrated = false;
        _smoothedDegrees = 0f;
        _isCentered = true;
        _sessionLeftMax = 0f;
        _sessionRightMax = 0f;
        _calibrationSamples = 0;

        Accelerometer.Default.ReadingChanged += OnAccelerometerReadingChanged;
        Accelerometer.Default.Start(sensorSpeed);

        Gyroscope.Default.ReadingChanged += OnGyroscopeReadingChanged;
        Gyroscope.Default.Start(sensorSpeed);

        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning) return;

        Accelerometer.Default.ReadingChanged -= OnAccelerometerReadingChanged;
        Accelerometer.Default.Stop();

        Gyroscope.Default.ReadingChanged -= OnGyroscopeReadingChanged;
        Gyroscope.Default.Stop();

        IsRunning = false;
    }

    // -------------------------------------------------------------------------
    // Accelerometer handler — provides absolute angle reference
    // -------------------------------------------------------------------------

    private void OnAccelerometerReadingChanged(object? sender, AccelerometerChangedEventArgs e)
    {
        float x = e.Reading.Acceleration.X;
        float y = e.Reading.Acceleration.Y;

        // atan2(X, Y): absolute clock-rotation angle from upright.
        // 12 o'clock portrait: X=0, Y≈1 → 0°. Rotate CW → X grows positive → positive degrees.
        _accelAngle = MathF.Atan2(x, y) * RadToDeg;

        if (!_calibrated)
        {
            _gyroAngle = _accelAngle;
            _centerAngle = _accelAngle;
            _smoothedDegrees = 0f;
            _prevGyroTimestamp = Stopwatch.GetTimestamp();
            _calibrated = true;
        }
    }

    // -------------------------------------------------------------------------
    // Gyroscope handler — integration + filter + emit
    // -------------------------------------------------------------------------

    private void OnGyroscopeReadingChanged(object? sender, GyroscopeChangedEventArgs e)
    {
        if (!_calibrated) return;

        var now = Stopwatch.GetTimestamp();
        float rawDt = (float)(now - _prevGyroTimestamp) / Stopwatch.Frequency;
        _prevGyroTimestamp = now;

        // Guard against absurd deltas (app resume, first tick), then EMA-smooth
        if (rawDt <= 0f || rawDt > 0.05f) rawDt = 0.01f;
        _smoothedDt = 0.7f * _smoothedDt + 0.3f * rawDt;
        float dt = _smoothedDt;

        // Negate Z: CW rotation (steering right) gives negative Z by right-hand rule
        float gyroRate = -e.Reading.AngularVelocity.Z; // rad/s

        // Integrate gyro
        _gyroAngle += gyroRate * RadToDeg * dt;

        // Dynamic complementary filter: trust accel more when still, gyro more when moving fast
        float gyroRateDeg = gyroRate * RadToDeg;
        float accelWeight =
            MathF.Abs(gyroRateDeg) < 10f ? 0.03f :
            MathF.Abs(gyroRateDeg) < 50f ? 0.01f :
            0.001f;
        _gyroAngle = (1f - accelWeight) * _gyroAngle + accelWeight * _accelAngle;

        // Near-neutral drift correction: when nearly still and near center,
        // silently pull gyroAngle toward the accel absolute reference.
        // Condition uses relative angle (bug fix: accelAngle is absolute, not relative to center).
        float steeringRelative = _gyroAngle - _centerAngle;
        if (MathF.Abs(gyroRateDeg) < 0.5f && MathF.Abs(steeringRelative) < 2f)
            _gyroAngle += (_accelAngle - _gyroAngle) * dt * 2f;

        // Hand-bias learning: when completely still and near center, slowly adapt centerAngle
        // toward the user's natural grip — ~20s time constant, invisible to the user
        if (MathF.Abs(gyroRateDeg) < 0.2f && MathF.Abs(steeringRelative) < 3f)
            _centerAngle += steeringRelative * dt * 0.05f;

        // Self-centering spring: constant small pull toward center, mimics real wheel return force
        _gyroAngle -= steeringRelative * CenterAssist * dt;

        // Steering degrees relative to calibrated center
        float steeringDegrees = _gyroAngle - _centerAngle;

        // Auto range calibration: track per-session extremes, require 120 meaningful samples
        // before updating RangeDegrees, clamp to safe bounds — resets on Recenter()
        if (AutoCalibrate && MathF.Abs(steeringDegrees) > 5f)
        {
            _sessionLeftMax  = MathF.Min(_sessionLeftMax,  steeringDegrees);
            _sessionRightMax = MathF.Max(_sessionRightMax, steeringDegrees);
            _calibrationSamples++;

            if (_calibrationSamples >= 120)
            {
                float observed = MathF.Max(MathF.Abs(_sessionLeftMax), MathF.Abs(_sessionRightMax));
                RangeDegrees = Math.Clamp(observed, 15f, 90f);
            }
        }

        // Adaptive smoothing: high alpha from first movement so response isn't delayed at turn start
        float absRateDeg = MathF.Abs(gyroRateDeg);
        float alpha =
            absRateDeg > 5f ? 0.9f :
            absRateDeg > 1f ? 0.6f :
            0.2f;
        _smoothedDegrees = (1f - alpha) * _smoothedDegrees + alpha * steeringDegrees;

        // Hysteresis deadzone: enter center at DeadZone, exit at DeadZone*1.5 — eliminates flicker
        float absSmoothed = MathF.Abs(_smoothedDegrees);
        if (_isCentered)
        {
            if (absSmoothed > DeadZone * 1.5f) _isCentered = false;
        }
        else
        {
            if (absSmoothed < DeadZone) _isCentered = true;
        }
        float outputDegrees = _isCentered ? 0f : _smoothedDegrees;

        CurrentAngleDegrees = outputDegrees;
        // No hard clamp — let tanh provide smooth saturation beyond RangeDegrees
        float linear = outputDegrees * Sensitivity / RangeDegrees;
        float curved = MathF.Sign(linear) * MathF.Pow(MathF.Abs(linear), SteeringExpo);
        CurrentValue = MathF.Tanh(curved * SteeringGain);

        SteeringChanged?.Invoke(this, CurrentValue);
    }

    // -------------------------------------------------------------------------
    // Manual controls
    // -------------------------------------------------------------------------

    /// <summary>
    /// Set the current phone position as the new center.
    /// Hold the phone at your natural neutral steering position and press this.
    /// Does NOT reset gyro state — filter continues uninterrupted.
    /// </summary>
    public void Recenter()
    {
        _centerAngle = _gyroAngle;
        _smoothedDegrees = 0f;
        _isCentered = true;
        _sessionLeftMax = 0f;
        _sessionRightMax = 0f;
        _calibrationSamples = 0;
        CurrentAngleDegrees = 0f;
        CurrentValue = 0f;
        Recentered?.Invoke(this, EventArgs.Empty);
        SteeringChanged?.Invoke(this, 0f);
    }

    public void ResetSettings()
    {
        Sensitivity = 1.4f;
        RangeDegrees = 45f;
        DeadZone = 0.5f;
        SteeringExpo = 1.2f;
        SteeringGain = 2.5f;
        CenterAssist = 0.02f;
    }

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

    public SteeringDiagnostics GetDiagnostics() => new()
    {
        AngleDegrees = CurrentAngleDegrees,
        NormalizedValue = CurrentValue,
        Sensitivity = Sensitivity,
        RangeDegrees = RangeDegrees,
        IsRunning = IsRunning,
    };
}

public record SteeringDiagnostics
{
    public float AngleDegrees { get; init; }
    public float NormalizedValue { get; init; }
    public float Sensitivity { get; init; }
    public float RangeDegrees { get; init; }
    public bool IsRunning { get; init; }

    public override string ToString() =>
        $"Angle: {AngleDegrees:F1}°  |  Value: {NormalizedValue:F3}  |  " +
        $"Sensitivity: {Sensitivity:F1}  |  Range: ±{RangeDegrees:F0}°  |  " +
        $"Running: {IsRunning}";
}
