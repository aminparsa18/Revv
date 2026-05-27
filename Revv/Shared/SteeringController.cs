using System.Numerics;

namespace Revv.Shared;

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

    /// <summary>tanh gain. Controls saturation softness. Default: 2.5.</summary>
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
    // Internal state
    // -------------------------------------------------------------------------

    // Reference quaternion: the orientation the user set as "straight ahead".
    // Recenter() updates this. All steering angles are relative to it.
    private Quaternion _referenceOrientation = Quaternion.Identity;
    private Quaternion _lastOrientation = Quaternion.Identity;
    private bool _calibrated = false;

    private float _degrees = 0f;
    private bool _isCentered = true;
    private float _sessionLeftMax = 0f;
    private float _sessionRightMax = 0f;
    private int _calibrationSamples = 0;

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

        if (!OrientationSensor.Default.IsSupported)
            throw new NotSupportedException("This device does not support the orientation sensor.");

        _calibrated = false;
        _degrees = 0f;
        _isCentered = true;
        _sessionLeftMax = 0f;
        _sessionRightMax = 0f;
        _calibrationSamples = 0;

        OrientationSensor.Default.ReadingChanged += OnOrientationChanged;
        OrientationSensor.Default.Start(sensorSpeed);

        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning) return;

        OrientationSensor.Default.ReadingChanged -= OnOrientationChanged;
        OrientationSensor.Default.Stop();

        IsRunning = false;
    }

    // -------------------------------------------------------------------------
    // Orientation handler
    // -------------------------------------------------------------------------

    private void OnOrientationChanged(object? sender, OrientationSensorChangedEventArgs e)
    {
        var q = e.Reading.Orientation;
        _lastOrientation = q;

        // First reading: snapshot as the neutral reference, then wait for next tick
        if (!_calibrated)
        {
            _referenceOrientation = q;
            _calibrated = true;
            return;
        }

        // Relative rotation from reference to current, expressed in the reference device frame.
        // q_rel = q_ref⁻¹ * q_curr  →  rotation around device Z = steering wheel rotation.
        var delta = Quaternion.Inverse(_referenceOrientation) * q;

        // Extract Z-axis twist. CW rotation (steer right) is negative Z by right-hand rule, so negate.
        _degrees = ZTwistDegrees(delta);

        // Self-centering spring: gentle decay toward zero each frame.
        _degrees *= 1f - CenterAssist;

        // Auto range calibration
        if (AutoCalibrate && MathF.Abs(_degrees) > 5f)
        {
            _sessionLeftMax  = MathF.Min(_sessionLeftMax,  _degrees);
            _sessionRightMax = MathF.Max(_sessionRightMax, _degrees);
            _calibrationSamples++;

            if (_calibrationSamples >= 120)
            {
                float observed = MathF.Max(MathF.Abs(_sessionLeftMax), MathF.Abs(_sessionRightMax));
                RangeDegrees = Math.Clamp(observed, 15f, 90f);
            }
        }

        // Hysteresis deadzone: enter center at DeadZone, exit at DeadZone×1.5 — eliminates flicker
        float absDegrees = MathF.Abs(_degrees);
        if (_isCentered)
        {
            if (absDegrees > DeadZone * 1.5f) _isCentered = false;
        }
        else
        {
            if (absDegrees < DeadZone) _isCentered = true;
        }
        float outputDegrees = _isCentered ? 0f : _degrees;

        CurrentAngleDegrees = outputDegrees;
        float linear = outputDegrees * Sensitivity / RangeDegrees;
        float curved = MathF.Sign(linear) * MathF.Pow(MathF.Abs(linear), SteeringExpo);
        CurrentValue = MathF.Tanh(curved * SteeringGain);

        SteeringChanged?.Invoke(this, CurrentValue);
    }

    // Swing-twist decomposition: angle of rotation around the quaternion's local Z axis.
    // Returns signed degrees in (-180, 180).
    private static float ZTwistDegrees(Quaternion q)
    {
        // Project vector part onto Z axis to isolate the twist component.
        // Guard: if both Z and W are zero the quaternion is degenerate (shouldn't happen in practice).
        if (q.Z * q.Z + q.W * q.W < 1e-12f) return 0f;
        return 2f * MathF.Atan2(q.Z, q.W) * RadToDeg;
    }

    // -------------------------------------------------------------------------
    // Manual controls
    // -------------------------------------------------------------------------

    /// <summary>
    /// Set the current phone position as the new center.
    /// Hold the phone at your natural neutral steering position and press this.
    /// Snapshots the current quaternion as the new reference — instant, no filter disruption.
    /// </summary>
    public void Recenter()
    {
        if (_calibrated)
            _referenceOrientation = _lastOrientation;

        _degrees = 0f;
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
