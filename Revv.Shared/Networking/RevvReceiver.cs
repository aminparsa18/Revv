using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Revv.Shared.Networking;

public class RevvReceiver : IAsyncDisposable
{
    public int StaleTimeoutMs { get; set; } = 3000;

    public ReceiverState State { get; private set; } = ReceiverState.Idle;
    public string? ConnectedPhoneIp { get; private set; }
    public float LatestSteering { get; private set; } = 0f;
    public float LatestThrottle { get; private set; } = 0f;
    public float LatestBrake { get; private set; } = 0f;
    public long PacketsReceived { get; private set; } = 0;

    public event EventHandler<string>? PhoneConnected;
    public event EventHandler? PhoneDisconnected;
    public event EventHandler<float>? SteeringReceived;
    public event EventHandler<float>? ThrottleReceived;
    public event EventHandler<float>? BrakeReceived;
    public event EventHandler<Exception>? ErrorOccurred;

    private UdpClient? _discoveryListener;
    private UdpClient? _dataListener;
    private CancellationTokenSource? _cts;
    private DateTime _lastPacketTime = DateTime.MinValue;

    public Task StartAsync()
    {
        if (State != ReceiverState.Idle) return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        _ = EnterDiscoveryAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        await Task.Delay(100);
        Cleanup();
        State = ReceiverState.Idle;
        LatestSteering = 0f;
        LatestThrottle = 0f;
        LatestBrake = 0f;
    }

    private async Task EnterDiscoveryAsync(CancellationToken ct)
    {
        State = ReceiverState.WaitingForPhone;
        ConnectedPhoneIp = null;

        try
        {
            _discoveryListener = new UdpClient(RevvDiscovery.BroadcastPort);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _discoveryListener.ReceiveAsync(ct);
                var message = Encoding.UTF8.GetString(result.Buffer);

                if (RevvDiscovery.TryParseBeacon(message, out _))
                {
                    var phoneIp = result.RemoteEndPoint.Address.ToString();
                    var ackBytes = Encoding.UTF8.GetBytes(RevvDiscovery.AckMessage);
                    var phoneAckEndpoint = new IPEndPoint(
                        result.RemoteEndPoint.Address,
                        RevvDiscovery.PhoneListenPort);

                    await _discoveryListener.SendAsync(ackBytes, ackBytes.Length, phoneAckEndpoint);

                    _discoveryListener.Close();
                    ConnectedPhoneIp = phoneIp;
                    await EnterReceivingAsync(phoneIp, ct);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
        }
    }

    private async Task EnterReceivingAsync(string phoneIp, CancellationToken ct)
    {
        State = ReceiverState.Receiving;
        PhoneConnected?.Invoke(this, phoneIp);
        _lastPacketTime = DateTime.UtcNow;

        _dataListener = new UdpClient(RevvDiscovery.DataPort);

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && State == ReceiverState.Receiving)
            {
                await Task.Delay(500, ct);
                var elapsed = (DateTime.UtcNow - _lastPacketTime).TotalMilliseconds;

                if (elapsed > StaleTimeoutMs)
                {
                    HandleDisconnect(ct);
                    return;
                }
            }
        }, ct);

        try
        {
            while (!ct.IsCancellationRequested && State == ReceiverState.Receiving)
            {
                var result = await _dataListener.ReceiveAsync(ct);

                if (result.RemoteEndPoint.Address.ToString() != phoneIp) continue;
                if (result.Buffer.Length < 4) continue;

                var packet = RevvPacket.FromBytes(result.Buffer);
                LatestSteering = packet.SteeringValue;
                LatestThrottle = packet.ThrottleValue;
                LatestBrake = packet.BrakeValue;
                PacketsReceived++;
                _lastPacketTime = DateTime.UtcNow;

                SteeringReceived?.Invoke(this, packet.SteeringValue);
                ThrottleReceived?.Invoke(this, packet.ThrottleValue);
                BrakeReceived?.Invoke(this, packet.BrakeValue);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
            HandleDisconnect(ct);
        }
    }

    private void HandleDisconnect(CancellationToken ct)
    {
        if (State != ReceiverState.Receiving) return;

        LatestSteering = 0f;
        LatestThrottle = 0f;
        LatestBrake = 0f;
        PhoneDisconnected?.Invoke(this, EventArgs.Empty);

        try { _dataListener?.Close(); } catch { }
        _dataListener = null;

        if (!ct.IsCancellationRequested)
            _ = EnterDiscoveryAsync(ct);
    }

    private void Cleanup()
    {
        try { _discoveryListener?.Close(); } catch { }
        try { _dataListener?.Close(); } catch { }
        _discoveryListener = null;
        _dataListener = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}

public enum ReceiverState
{
    Idle,
    WaitingForPhone,
    Receiving
}
