using Revv.Shared;
using Revv.Shared.Networking;

namespace Revv;

public partial class MainPage : ContentPage
{
    private readonly SteeringController _steering;
    private readonly RevvBroadcaster _broadcaster;

    private bool _settingsOpen = false;
    private bool _isConnected = false;
    private bool _pedalsEnabled = false;

    public MainPage(SteeringController steering, RevvBroadcaster broadcaster)
    {
        InitializeComponent();

        _steering = steering;
        _broadcaster = broadcaster;

        WireSteeringEvents();
        WireNetworkEvents();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        DeviceDisplay.Current.KeepScreenOn = true;

        try
        {
            _steering.Start();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"GYRO ERR: {ex.Message[..Math.Min(ex.Message.Length, 20)]}";
            return;
        }

        try
        {
            await _broadcaster.StartAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"NET ERR: {ex.Message[..Math.Min(ex.Message.Length, 22)]}";
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();

        DeviceDisplay.Current.KeepScreenOn = false;

        _steering.Stop();
        await _broadcaster.StopAsync();
    }

    private void WireSteeringEvents()
    {
        _steering.SteeringChanged += (_, value) =>
        {
            _broadcaster.SetSteering(value);
            MainThread.BeginInvokeOnMainThread(() => UpdateWheelVisual(value));
        };

        _steering.Recentered += (_, _) =>
            MainThread.BeginInvokeOnMainThread(FlashRecenterFeedback);
    }

    private void WireNetworkEvents()
    {
        _broadcaster.Connected += (_, ip) =>
            MainThread.BeginInvokeOnMainThread(() => SetConnectedState(true, ip));

        _broadcaster.Disconnected += (_, _) =>
            MainThread.BeginInvokeOnMainThread(() => SetConnectedState(false));

        _broadcaster.ErrorOccurred += (_, ex) =>
            MainThread.BeginInvokeOnMainThread(() =>
                StatusLabel.Text = $"ERR: {ex.Message[..Math.Min(ex.Message.Length, 24)]}");
    }

    private void UpdateWheelVisual(float normalizedValue)
    {
        float angleDeg = normalizedValue * _steering.RangeDegrees;
        WheelRoot.Rotation = angleDeg;

        AngleLabel.Text = $"{normalizedValue:+0.000;-0.000; 0.000}";

        float absValue = MathF.Abs(normalizedValue);
        AngleLabel.TextColor = absValue > 0.85f
            ? Color.FromArgb("#E8001D")
            : Color.FromArgb("#555555");
    }

    private void SetConnectedState(bool connected, string? ip = null)
    {
        _isConnected = connected;

        if (connected)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#00E87A"));
            StatusLabel.Text = ip is not null ? $"PC  {ip}" : "CONNECTED";
            StatusLabel.TextColor = Color.FromArgb("#00E87A");
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#555555"));
            StatusLabel.Text = "SEARCHING...";
            StatusLabel.TextColor = Color.FromArgb("#555555");

            // Zero pedals on disconnect
            if (_pedalsEnabled)
            {
                _broadcaster.SetThrottle(0f);
                _broadcaster.SetBrake(0f);
                ResetBrakeVisual();
                ResetThrottleVisual();
            }
        }
    }

    private async void FlashRecenterFeedback()
    {
        await WheelRoot.ScaleTo(0.95, 80, Easing.CubicOut);
        await WheelRoot.ScaleTo(1.0, 120, Easing.CubicIn);
    }

    // -------------------------------------------------------------------------
    // Gesture & control handlers
    // -------------------------------------------------------------------------

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        _steering.Recenter();
    }

    private void OnSettingsToggled(object? sender, EventArgs e)
    {
        _settingsOpen = !_settingsOpen;
        SettingsPanel.IsVisible = _settingsOpen;
    }

    private void OnSensitivityChanged(object? sender, ValueChangedEventArgs e)
    {
        _steering.Sensitivity = (float)e.NewValue;
        SensitivityValueLabel.Text = $"{e.NewValue:F1}";
    }

    private void OnRangeChanged(object? sender, ValueChangedEventArgs e)
    {
        _steering.RangeDegrees = (float)e.NewValue;
        RangeValueLabel.Text = $"{(int)e.NewValue}°";
    }

    private void OnRecenterClicked(object? sender, EventArgs e)
    {
        _steering.Recenter();
        _settingsOpen = false;
        SettingsPanel.IsVisible = false;
    }

    // -------------------------------------------------------------------------
    // Pedals toggle
    // -------------------------------------------------------------------------

    private void OnPedalsToggled(object? sender, ToggledEventArgs e)
    {
        _pedalsEnabled = e.Value;
        BrakePanel.IsVisible = _pedalsEnabled;
        ThrottlePanel.IsVisible = _pedalsEnabled;

        if (!_pedalsEnabled)
        {
            _broadcaster.SetThrottle(0f);
            _broadcaster.SetBrake(0f);
        }
    }

    // -------------------------------------------------------------------------
    // Brake press / release
    // -------------------------------------------------------------------------

    private void OnBrakePressed(object? sender, EventArgs e)
    {
        _broadcaster.SetBrake(1f);
        BrakeBg.BackgroundColor = Color.FromArgb("#250005");
        BrakeIcon.TextColor = Color.FromArgb("#E8001D");
        BrakeValueLabel.Text = "●";
        BrakeValueLabel.TextColor = Color.FromArgb("#E8001D");
    }

    private void OnBrakeReleased(object? sender, EventArgs e)
    {
        _broadcaster.SetBrake(0f);
        ResetBrakeVisual();
    }

    private void ResetBrakeVisual()
    {
        BrakeBg.BackgroundColor = Color.FromArgb("#141414");
        BrakeIcon.TextColor = Color.FromArgb("#1F1F1F");
        BrakeValueLabel.Text = "—";
        BrakeValueLabel.TextColor = Color.FromArgb("#555555");
    }

    // -------------------------------------------------------------------------
    // Throttle press / release
    // -------------------------------------------------------------------------

    private void OnThrottlePressed(object? sender, EventArgs e)
    {
        _broadcaster.SetThrottle(1f);
        ThrottleBg.BackgroundColor = Color.FromArgb("#002810");
        ThrottleIcon.TextColor = Color.FromArgb("#00E87A");
        ThrottleValueLabel.Text = "●";
        ThrottleValueLabel.TextColor = Color.FromArgb("#00E87A");
    }

    private void OnThrottleReleased(object? sender, EventArgs e)
    {
        _broadcaster.SetThrottle(0f);
        ResetThrottleVisual();
    }

    private void ResetThrottleVisual()
    {
        ThrottleBg.BackgroundColor = Color.FromArgb("#141414");
        ThrottleIcon.TextColor = Color.FromArgb("#1F1F1F");
        ThrottleValueLabel.Text = "—";
        ThrottleValueLabel.TextColor = Color.FromArgb("#555555");
    }
}
