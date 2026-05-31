#if ANDROID
using Android.OS;
using Revv.Controls;

#endif
using Revv.Controls;
using Revv.Shared;
using Revv.Shared.Networking;
using RevvBtn = Revv.Shared.Networking.RevvButtonMask;

namespace Revv;

public partial class MainPage : ContentPage
{
    private readonly SteeringController _steering;
    private readonly RevvBroadcaster _broadcaster;

    private bool _settingsOpen = false;
    private bool _pedalsEnabled = false;
    private bool _isPaused = false;
    private CancellationTokenSource? _pulseCts;
    private long _lastUiUpdateMs;

#if ANDROID
    private Android.Net.Wifi.WifiManager? _wifiManager;
    private System.Timers.Timer? _wifiTimer;
#endif

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
        RefreshBattery();
        Battery.Default.BatteryInfoChanged += OnBatteryInfoChanged;

        // Clear any stale error or connected state from before background/screen-off.
        SetConnectedState(false);
        StartWifiPolling();

        try
        {
            _steering.Start();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"SENSOR ERR: {ex.Message[..Math.Min(ex.Message.Length, 16)]}";
            return;
        }

        try
        {
            await _broadcaster.StartAsync();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"NET ERR: {ex.Message[..Math.Min(ex.Message.Length, 20)]}";
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        Battery.Default.BatteryInfoChanged -= OnBatteryInfoChanged;
        DeviceDisplay.Current.KeepScreenOn = false;
        StopStatusPulse();
        StopWifiPolling();
        _steering.Stop();
        await _broadcaster.StopAsync();
    }

    // -------------------------------------------------------------------------
    // Event wiring
    // -------------------------------------------------------------------------

    private void WireSteeringEvents()
    {
        _steering.SteeringChanged += (_, value) =>
        {
            if (_pedalsEnabled)
                _broadcaster.SetSteering(value);

            // Throttle UI repaints to ~60 fps. Broadcaster always gets data at full sensor rate.
            long now = System.Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastUiUpdateMs) >= 16)
            {
                Interlocked.Exchange(ref _lastUiUpdateMs, now);
                MainThread.BeginInvokeOnMainThread(() => UpdateHorizonVisual(value));
            }
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
                StatusLabel.Text = $"ERR: {ex.Message[..Math.Min(ex.Message.Length, 22)]}");

        _broadcaster.LatencyUpdated += (_, ms) =>
            MainThread.BeginInvokeOnMainThread(() => UpdateLatency(ms));

        _broadcaster.RumbleReceived += OnRumbleReceived;
    }

    private void OnRumbleReceived(object? sender, (byte Large, byte Small) e)
    {
        byte amplitude = Math.Max(e.Large, e.Small);

#if ANDROID
        try
        {
            Vibrator? vibrator = Android.App.Application.Context
                .GetSystemService(Android.Content.Context.VibratorService)
                as Vibrator;
            if (vibrator?.HasVibrator != true)
            {
                return;
            }

            if (amplitude == 0)
            {
                vibrator.Cancel();
                return;
            }

            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                VibrationEffect? effect = VibrationEffect.CreateOneShot(80, amplitude);
                vibrator.Vibrate(effect);
            }
            else
            {
#pragma warning disable CA1422
                vibrator.Vibrate(80);
#pragma warning restore CA1422
            }
        }
        catch { }
#endif
    }

    // -------------------------------------------------------------------------
    // Horizon visual
    // -------------------------------------------------------------------------

    private void UpdateHorizonVisual(float normalizedValue)
    {
        float angleDeg = normalizedValue * _steering.RangeDegrees;
        AttitudeView.SetRoll(angleDeg);

        AngleLabel.Text = $"{normalizedValue:+0.000;-0.000; 0.000}";
        AngleLabel.TextColor = MathF.Abs(normalizedValue) > 0.85f
            ? Color.FromArgb("#FF9500")
            : Color.FromArgb("#555555");
    }

    private async void FlashRecenterFeedback()
    {
        await AttitudeView.ScaleToAsync(1.05, 80, Easing.CubicOut);
        await AttitudeView.ScaleToAsync(1.0, 120, Easing.SpringIn);
    }

    // -------------------------------------------------------------------------
    // Connection state
    // -------------------------------------------------------------------------

    private void SetConnectedState(bool connected, string? ip = null)
    {
        if (connected)
        {
            StopStatusPulse();
            StatusDot.Opacity = 1;
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#00E87A"));
            StatusLabel.Text = ip is not null ? $"PC  {ip}" : "CONNECTED";
            StatusLabel.TextColor = Color.FromArgb("#00E87A");
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromArgb("#555555"));
            StatusLabel.Text = "SEARCHING...";
            StatusLabel.TextColor = Color.FromArgb("#555555");
            LatencyValue.Text = "—";
            LatencyValue.TextColor = Color.FromArgb("#555555");
            StartStatusPulse();

            _broadcaster.SetRightStick(0f, 0f);
            _broadcaster.SetLeftStick(0f, 0f);

            if (_pedalsEnabled)
            {
                _broadcaster.SetThrottle(0f);
                _broadcaster.SetBrake(0f);
                ResetBrakeVisual();
                ResetThrottleVisual();
            }
        }
    }

    private void UpdateLatency(int ms)
    {
        LatencyValue.Text = $"{ms}ms";
        LatencyValue.TextColor = ms < 20
            ? Color.FromArgb("#00E87A")
            : ms < 60
            ? Color.FromArgb("#F0F0F0")
            : Color.FromArgb("#FF9500");
    }

    // -------------------------------------------------------------------------
    // Stats
    // -------------------------------------------------------------------------

    private void RefreshBattery()
    {
        try
        {
            double level = Battery.Default.ChargeLevel;
            BatteryState state = Battery.Default.State;
            bool charging = state == BatteryState.Charging;
            BatteryGauge.SetBattery((float)level, charging);
        }
        catch
        {
            BatteryGauge.SetBattery(-1f, false);
        }
    }

    private void OnBatteryInfoChanged(object? sender, BatteryInfoChangedEventArgs e)
        => MainThread.BeginInvokeOnMainThread(RefreshBattery);

    // -------------------------------------------------------------------------
    // Status dot pulse animation
    // -------------------------------------------------------------------------

    private void StartStatusPulse()
    {
        _pulseCts?.Cancel();
        _pulseCts = new CancellationTokenSource();
        _ = PulseLoop(_pulseCts.Token);
    }

    private void StopStatusPulse()
    {
        _pulseCts?.Cancel();
        _pulseCts = null;
        _ = StatusDot.FadeToAsync(1.0, 0); // snap to opaque, cancels any in-flight fade
    }

    private async Task PulseLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await StatusDot.FadeToAsync(0.2, 600);
            if (token.IsCancellationRequested)
            {
                break;
            }

            await StatusDot.FadeToAsync(1.0, 600);
        }
    }

    // -------------------------------------------------------------------------
    // WiFi signal polling (Android)
    // -------------------------------------------------------------------------

    private void StartWifiPolling()
    {
#if ANDROID
        _wifiManager = Android.App.Application.Context
            .GetSystemService(Android.Content.Context.WifiService)
            as Android.Net.Wifi.WifiManager;
        RefreshWifiSignal();
        _wifiTimer = new System.Timers.Timer(3000) { AutoReset = true };
        _wifiTimer.Elapsed += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshWifiSignal);
        _wifiTimer.Start();
#endif
    }

    private void StopWifiPolling()
    {
#if ANDROID
        _wifiTimer?.Stop();
        _wifiTimer?.Dispose();
        _wifiTimer = null;
#endif
    }

    private void RefreshWifiSignal()
    {
#if ANDROID
        try
        {
            if (_wifiManager?.IsWifiEnabled != true)
            {
                WifiView.SetSignal(0);
                return;
            }
            int rssi = _wifiManager.ConnectionInfo?.Rssi ?? -127;
            if (rssi <= -100)
            {
                WifiView.SetSignal(0);
                return;
            }
            // CalculateSignalLevel(rssi, 4) returns 0-3; deprecated in API 30 but functional.
#pragma warning disable CA1422
            int level = Android.Net.Wifi.WifiManager.CalculateSignalLevel(rssi, 4);
#pragma warning restore CA1422
            WifiView.SetSignal(level);
        }
        catch
        {
            WifiView.SetSignal(0);
        }
#endif
    }

    // -------------------------------------------------------------------------
    // Gesture & settings handlers
    // -------------------------------------------------------------------------

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => _steering.Recenter();

    private async void OnPauseClicked(object? sender, EventArgs e)
    {
        _isPaused = !_isPaused;
        PauseButton.Source = _isPaused ? "play.svg" : "pause.svg";

        _broadcaster.SetButton(RevvBtn.Start, true);
        await Task.Delay(120);
        _broadcaster.SetButton(RevvBtn.Start, false);
    }

    private bool _panelAnimating;

    private async void OnSettingsToggled(object? sender, EventArgs e)
    {
        if (_panelAnimating)
        {
            return;
        }

        _panelAnimating = true;
        _settingsOpen = !_settingsOpen;

        if (!_settingsOpen)
            TooltipBorder.IsVisible = false;

        View incoming = _settingsOpen ? SettingsPanel : AttitudePanel;
        View outgoing = _settingsOpen ? AttitudePanel : SettingsPanel;

        incoming.Opacity = 0;
        incoming.IsVisible = true;

        await Task.WhenAll(outgoing.FadeToAsync(0, 220), incoming.FadeToAsync(1, 220));

        outgoing.IsVisible = false;
        _panelAnimating = false;
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

    private void OnDeadZoneChanged(object? sender, ValueChangedEventArgs e)
    {
        _steering.DeadZone = (float)e.NewValue;
        DeadZoneValueLabel.Text = $"{e.NewValue:F1}°";
    }

    private void OnCenterAssistChanged(object? sender, ValueChangedEventArgs e)
    {
        _steering.CenterAssist = (float)e.NewValue / 100f;
        CenterAssistValueLabel.Text = e.NewValue == 0 ? "OFF" : $"{e.NewValue:F0}%";
    }

    private void OnSensitivityHelp(object? sender, EventArgs e)
        => ShowTooltip("Amplifies gyro input. Higher = full lock with less physical rotation.");

    private void OnRangeHelp(object? sender, EventArgs e)
        => ShowTooltip("Physical steering arc that maps to full lock. Narrow = twitchy, wide = relaxed.");

    private void OnDeadZoneHelp(object? sender, EventArgs e)
        => ShowTooltip("Ignores input smaller than this angle. Reduces drift when holding still.");

    private void OnCenterAssistHelp(object? sender, EventArgs e)
        => ShowTooltip("Spring that pulls steering toward center when you stop turning. Higher = stronger. 0% = off.");

    private async void ShowTooltip(string text)
    {
        TooltipLabel.Text = text;
        TooltipBorder.Opacity = 0;
        TooltipBorder.IsVisible = true;
        await TooltipBorder.FadeToAsync(1, 180);
    }

    private async void OnTooltipDismissed(object? sender, TappedEventArgs e)
    {
        await TooltipBorder.FadeToAsync(0, 150);
        TooltipBorder.IsVisible = false;
    }

    private void OnRecenterClicked(object? sender, EventArgs e)
    {
        _steering.Recenter();
        _settingsOpen = false;
        SettingsPanel.IsVisible = false;
        AttitudePanel.IsVisible = true;
    }

    private void OnJoystickChanged(object? sender, (float X, float Y) value)
        => _broadcaster.SetRightStick(value.X, value.Y);

    private void OnLeftJoystickChanged(object? sender, (float X, float Y) value)
        => _broadcaster.SetLeftStick(value.X, value.Y);

    private void OnFaceButtonPressed(object? sender, FaceButton button)
        => _broadcaster.SetButton(ToMask(button), true);

    private void OnFaceButtonReleased(object? sender, FaceButton button)
        => _broadcaster.SetButton(ToMask(button), false);

    private static RevvBtn ToMask(FaceButton button) => button switch
    {
        FaceButton.A => RevvBtn.A,
        FaceButton.B => RevvBtn.B,
        FaceButton.X => RevvBtn.X,
        FaceButton.Y => RevvBtn.Y,
        _            => RevvBtn.None,
    };

    // -------------------------------------------------------------------------
    // Pedals toggle
    // -------------------------------------------------------------------------

    private async void OnPedalsToggled(object? sender, TappedEventArgs e)
    {
        _pedalsEnabled = !_pedalsEnabled;

        if (_pedalsEnabled)
        {
            // Pedals mode ON: hide joystick, reveal throttle + brake + face buttons
            PedalsTrack.BackgroundColor = Color.FromArgb("#3A0008");
            PedalsTrack.Stroke          = Color.FromArgb("#E8001D");
            PedalsThumb.BackgroundColor = Color.FromArgb("#E8001D");

            _broadcaster.SetRightStick(0f, 0f);
            _broadcaster.SetLeftStick(0f, 0f);

            BrakePanel.TranslationX    = -80;
            ThrottlePanel.TranslationX =  80;
            BrakePanel.Opacity         = 0;
            ThrottlePanel.Opacity      = 0;
            BrakePanel.IsVisible       = true;
            ThrottlePanel.IsVisible    = true;

            await Task.WhenAll(
                PedalsThumb.TranslateToAsync(28, 0, 500, Easing.SpringOut),
                BrakePanel.TranslateToAsync(0, 0, 380, Easing.SpringOut),
                ThrottlePanel.TranslateToAsync(0, 0, 380, Easing.SpringOut),
                BrakePanel.FadeToAsync(1, 250),
                ThrottlePanel.FadeToAsync(1, 250),
                JoystickView.FadeToAsync(0, 200),
                LeftJoystickView.FadeToAsync(0, 200)
            );

            JoystickView.IsVisible     = false;
            LeftJoystickView.IsVisible = false;
        }
        else
        {
            // Pedals mode OFF: hide throttle + brake + face buttons, reveal joystick
            _broadcaster.SetSteering(0f);
            _broadcaster.SetThrottle(0f);
            _broadcaster.SetBrake(0f);
            PedalsTrack.BackgroundColor = Color.FromArgb("#1F1F1F");
            PedalsTrack.Stroke          = Color.FromArgb("#2A2A2A");
            PedalsThumb.BackgroundColor = Color.FromArgb("#555555");

            JoystickView.Opacity       = 0;
            JoystickView.IsVisible     = true;
            LeftJoystickView.Opacity   = 0;
            LeftJoystickView.IsVisible = true;

            await Task.WhenAll(
                PedalsThumb.TranslateToAsync(0, 0, 500, Easing.SpringOut),
                BrakePanel.TranslateToAsync(-80, 0, 220, Easing.CubicIn),
                ThrottlePanel.TranslateToAsync(80, 0, 220, Easing.CubicIn),
                BrakePanel.FadeToAsync(0, 180),
                ThrottlePanel.FadeToAsync(0, 180),
                JoystickView.FadeToAsync(1, 280),
                LeftJoystickView.FadeToAsync(1, 280)
            );

            BrakePanel.IsVisible       = false;
            ThrottlePanel.IsVisible    = false;
            BrakePanel.TranslationX    = 0;
            ThrottlePanel.TranslationX = 0;
            BrakePanel.Opacity         = 1;
            ThrottlePanel.Opacity      = 1;
        }
    }

    // -------------------------------------------------------------------------
    // Brake press / release
    // -------------------------------------------------------------------------

    private void OnBrakePressed(object? sender, PointerEventArgs e)
    {
        _broadcaster.SetBrake(1f);
        BrakePedalView.SetPressed(true);
        Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(45));
    }

    private void OnBrakeReleased(object? sender, PointerEventArgs e)
    {
        _broadcaster.SetBrake(0f);
        BrakePedalView.SetPressed(false);
    }

    private void ResetBrakeVisual() => BrakePedalView.SetPressed(false);

    // -------------------------------------------------------------------------
    // Throttle press / release
    // -------------------------------------------------------------------------

    private void OnThrottlePressed(object? sender, PointerEventArgs e)
    {
        _broadcaster.SetThrottle(1f);
        ThrottlePedalView.SetPressed(true);
        Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(45));
    }

    private void OnThrottleReleased(object? sender, PointerEventArgs e)
    {
        _broadcaster.SetThrottle(0f);
        ThrottlePedalView.SetPressed(false);
    }

    private void ResetThrottleVisual() => ThrottlePedalView.SetPressed(false);
}
