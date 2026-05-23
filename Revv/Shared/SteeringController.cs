namespace Revv.Shared;

/// <summary>
/// Converts raw gyroscope angular velocity into a normalized steering value (-1.0 to +1.0).
/// Designed for portrait hold: player tilts phone left/right like a steering wheel.
/// </summary>
public class SteeringController
{
    // -------------------------------------------------------------------------
    // User-configurable settings
    // -------------------------------------------------------------------------

    /// <summary>
    /// Multiplier applied to angular velocity before integration.
    /// Higher = more responsive. Recommended range: 0.1 – 3.0. Default: 1.0.
    /// </summary>
    public float Sensitivity { get; set; } = 1.0f;

    /// <summary>
    /// Maximum physical tilt angle (in degrees) that maps to full lock (±1.0).
    /// Lower = arcade (small flick = full turn). Higher = sim (big movement needed).
    /// Recommended range: 15 – 90. Default: 45.
    /// </summary>
    public float RangeDegrees { get; set; } = 45f;

    /// <summary>
    /// Minimum angular velocity (rad/s) below which input is ignored.
    /// Filters out sensor noise when the phone is nearly still.
    /// </summary>
    public float DeadZone { get; set; } = 0.02f;

    /// <summary>
    /// Angular velocity threshold (rad/s) below which passive re-centering activates.
    /// </summary>
    public float RecenterThreshold { get; set; } = 0.05f;

    /// <summary>
    /// Multiplier applied to current angle each frame when input is below RecenterThreshold.
    /// 0.97 = bleeds ~3% toward center per frame. Range: 0.90 – 0.99.
    /// </summary>
    public float RecenterStrength { get; set; } = 0.97f;

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    /// <summary>Current integrated steering angle in degrees. Clamped to ±RangeDegrees.</summary>
    public float CurrentAngleDegrees { get; private set; } = 0f;

    /// <summary>Last normalized output value. -1.0 = full left, +1.0 = full right.</summary>
    public float CurrentValue { get; private set; } = 0f;

    /// <summary>Whether the controller is actively receiving input.</summary>
    public bool IsRunning { get; private set; } = false;

    private DateTime _lastUpdate = DateTime.UtcNow;
    private bool _firstReading = true;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Fires every time a new steering value is computed. Payload: -1.0 to +1.0.</summary>
    public event EventHandler<float>? SteeringChanged;

    /// <summary>Fires when the wheel is recentered (manually or via auto-center).</summary>
    public event EventHandler? Recentered;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Start listening to the gyroscope. Call once when the steering screen appears.
    /// </summary>
    /// <param name="sensorSpeed">How fast the gyroscope reports. Default: Game (fastest).</param>
    public void Start(SensorSpeed sensorSpeed = SensorSpeed.Game)
    {
        if (IsRunning) return;

        if (!Gyroscope.Default.IsSupported)
            throw new NotSupportedException("This device does not have a gyroscope.");

        Gyroscope.Default.ReadingChanged += OnGyroscopeReadingChanged;
        Gyroscope.Default.Start(sensorSpeed);

        _firstReading = true;
        IsRunning = true;
    }

    /// <summary>
    /// Stop listening to the gyroscope. Call when navigating away from the steering screen.
    /// </summary>
    public void Stop()
    {
        if (!IsRunning) return;

        Gyroscope.Default.ReadingChanged -= OnGyroscopeReadingChanged;
        Gyroscope.Default.Stop();

        IsRunning = false;
    }

    // -------------------------------------------------------------------------
    // Core processing
    // -------------------------------------------------------------------------

    private void OnGyroscopeReadingChanged(object? sender, GyroscopeChangedEventArgs e)
    {
        float value = ProcessReading(e.Reading);
        SteeringChanged?.Invoke(this, value);
    }

    /// <summary>
    /// Process a single gyroscope reading and return the normalized steering value.
    /// Can also be called manually (e.g. in tests or when injecting readings).
    /// </summary>
    public float ProcessReading(GyroscopeData data)
    {
        var now = DateTime.UtcNow;

        // Skip delta-time on the very first reading to avoid a huge jump
        if (_firstReading)
        {
            _lastUpdate = now;
            _firstReading = false;
            return CurrentValue;
        }

        float deltaTime = (float)(now - _lastUpdate).TotalSeconds;
        _lastUpdate = now;

        // Guard against absurd delta (e.g. app resumed from background)
        if (deltaTime > 0.1f) deltaTime = 0.1f;

        // Portrait hold: Y-axis = tilting phone left/right
        float angularVelocity = data.AngularVelocity.Y; // radians/sec

        // Dead zone — ignore noise when phone is still
        if (MathF.Abs(angularVelocity) < DeadZone)
            angularVelocity = 0f;

        // Integrate: angular velocity → angle
        float deltaDegrees = angularVelocity
            * (180f / MathF.PI)   // rad → degrees
            * deltaTime
            * Sensitivity;

        CurrentAngleDegrees += deltaDegrees;

        // Passive re-centering spring — pulls toward 0 when input is idle
        if (MathF.Abs(angularVelocity) < RecenterThreshold)
            CurrentAngleDegrees *= RecenterStrength;

        // Clamp to user-defined physical range
        CurrentAngleDegrees = Math.Clamp(CurrentAngleDegrees, -RangeDegrees, RangeDegrees);

        // Normalize to gamepad axis range: -1.0 (full left) → +1.0 (full right)
        CurrentValue = CurrentAngleDegrees / RangeDegrees;

        return CurrentValue;
    }

    // -------------------------------------------------------------------------
    // Manual controls
    // -------------------------------------------------------------------------

    /// <summary>
    /// Instantly reset the steering to center.
    /// Call on double-tap or a dedicated recenter button.
    /// </summary>
    public void Recenter()
    {
        CurrentAngleDegrees = 0f;
        CurrentValue = 0f;
        Recentered?.Invoke(this, EventArgs.Empty);
        SteeringChanged?.Invoke(this, 0f);
    }

    /// <summary>
    /// Reset settings to defaults.
    /// </summary>
    public void ResetSettings()
    {
        Sensitivity = 1.0f;
        RangeDegrees = 45f;
        DeadZone = 0.02f;
        RecenterThreshold = 0.05f;
        RecenterStrength = 0.97f;
    }

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a snapshot of current state — useful for a debug overlay during development.
    /// </summary>
    public SteeringDiagnostics GetDiagnostics() => new()
    {
        AngleDegrees = CurrentAngleDegrees,
        NormalizedValue = CurrentValue,
        Sensitivity = Sensitivity,
        RangeDegrees = RangeDegrees,
        IsRunning = IsRunning,
    };
}

/// <summary>Snapshot of SteeringController state for debug overlays or telemetry.</summary>
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