using Revv.Shared.Networking;
using Revv.Windows.Input;

namespace Revv.Windows;

public class RevvSession : IAsyncDisposable
{
    public RevvReceiver Receiver { get; } = new();
    public RevvGamepad Gamepad { get; } = new();
    public SessionState State { get; private set; } = SessionState.Idle;

    public event EventHandler<string>? PhoneConnected;
    public event EventHandler? PhoneDisconnected;
    public event EventHandler<float>? SteeringUpdated;
    public event EventHandler<Exception>? ErrorOccurred;

    public Task StartAsync()
    {
        if (State != SessionState.Idle) return Task.CompletedTask;

        Gamepad.Connect();
        Gamepad.ErrorOccurred += (_, ex) => ErrorOccurred?.Invoke(this, ex);
        Gamepad.RumbleReceived += OnRumbleReceived;

        Receiver.PhoneConnected += OnPhoneConnected;
        Receiver.PhoneDisconnected += OnPhoneDisconnected;
        Receiver.SteeringReceived += OnSteeringReceived;
        Receiver.ThrottleReceived += (_, v) => Gamepad.SetThrottle(v);
        Receiver.BrakeReceived += (_, v) => Gamepad.SetBrake(v);
        Receiver.ErrorOccurred += (_, ex) => ErrorOccurred?.Invoke(this, ex);

        State = SessionState.WaitingForPhone;
        Receiver.StartAsync();
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        await Receiver.StopAsync();
        Gamepad.ZeroAllInputs();
        Gamepad.Disconnect();
        State = SessionState.Idle;
    }

    private void OnPhoneConnected(object? sender, string phoneIp)
    {
        State = SessionState.Active;
        PhoneConnected?.Invoke(this, phoneIp);
    }

    private void OnPhoneDisconnected(object? sender, EventArgs e)
    {
        Gamepad.ZeroAllInputs();
        State = SessionState.WaitingForPhone;
        PhoneDisconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnSteeringReceived(object? sender, float value)
    {
        Gamepad.SetSteering(value);
        SteeringUpdated?.Invoke(this, value);
    }

    private void OnRumbleReceived(object? sender, (byte Large, byte Small) e)
        => Receiver.SendRumble(e.Large, e.Small);

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Receiver.PhoneConnected -= OnPhoneConnected;
        Receiver.PhoneDisconnected -= OnPhoneDisconnected;
        Receiver.SteeringReceived -= OnSteeringReceived;
        Gamepad.RumbleReceived -= OnRumbleReceived;
        Gamepad.Dispose();
    }
}

public enum SessionState
{
    Idle,
    WaitingForPhone,
    Active
}
