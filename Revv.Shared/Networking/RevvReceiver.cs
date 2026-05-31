using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Revv.Shared.Networking;

public class RevvReceiver : IAsyncDisposable
{
    public int StaleTimeoutMs { get; set; } = 3000;

    public ReceiverState State { get; private set; } = ReceiverState.Idle;
    public string? ConnectedPhoneIp { get; private set; }
    public float  LatestSteering    { get; private set; } = 0f;
    public float  LatestThrottle    { get; private set; } = 0f;
    public float  LatestBrake       { get; private set; } = 0f;
    public ushort LatestButtons     { get; private set; } = 0;
    public float  LatestRightStickX { get; private set; } = 0f;
    public float  LatestRightStickY { get; private set; } = 0f;
    public float  LatestLeftStickX  { get; private set; } = 0f;
    public float  LatestLeftStickY  { get; private set; } = 0f;
    public long   PacketsReceived   { get; private set; } = 0;

    public event EventHandler<string>?        PhoneConnected;
    public event EventHandler?                PhoneDisconnected;
    public event EventHandler<float>?         SteeringReceived;
    public event EventHandler<float>?         ThrottleReceived;
    public event EventHandler<float>?         BrakeReceived;
    public event EventHandler<ushort>?        ButtonsReceived;
    public event EventHandler<(float X, float Y)>? RightStickReceived;
    public event EventHandler<(float X, float Y)>? LeftStickReceived;
    public event EventHandler<Exception>?           ErrorOccurred;

    private UdpClient? _discoveryListener;
    private UdpClient? _dataListener;
    private UdpClient? _echoSender;
    private UdpClient? _rumbleSender;
    private CancellationTokenSource? _cts;
    private DateTime _lastPacketTime = DateTime.MinValue;

    public void SendRumble(byte large, byte small)
    {
        if (State != ReceiverState.Receiving || ConnectedPhoneIp is null || _rumbleSender is null) return;
        try
        {
            var ep = new IPEndPoint(IPAddress.Parse(ConnectedPhoneIp), RevvDiscovery.RumblePort);
            _rumbleSender.Send(new[] { large, small }, 2, ep);
        }
        catch { }
    }

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
        State             = ReceiverState.Idle;
        LatestSteering    = 0f;
        LatestThrottle    = 0f;
        LatestBrake       = 0f;
        LatestButtons     = 0;
        LatestRightStickX = 0f;
        LatestRightStickY = 0f;
        LatestLeftStickX  = 0f;
        LatestLeftStickY  = 0f;
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
        _echoSender = new UdpClient();
        _rumbleSender = new UdpClient();
        var echoEndpoint = new IPEndPoint(IPAddress.Parse(phoneIp), RevvDiscovery.EchoPort);

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
                // Async-wait for the first available packet (efficient idle blocking)
                var result = await _dataListener.ReceiveAsync(ct);

                if (result.RemoteEndPoint.Address.ToString() != phoneIp) continue;
                if (result.Buffer.Length < 4) continue;

                var latestBuffer = result.Buffer;

                // Drain every packet already queued in the OS buffer — keep only the newest.
                // This kills stale backlog instantly when the user changes direction.
                var drainEp = new IPEndPoint(IPAddress.Any, 0);
                while (_dataListener.Available >= RevvPacket.SizeBytes)
                {
                    var drainBuffer = _dataListener.Receive(ref drainEp);
                    if (drainEp.Address.ToString() == phoneIp && drainBuffer.Length >= RevvPacket.SizeBytes)
                        latestBuffer = drainBuffer;
                }

                // Echo the tick from the freshest packet for RTT measurement
                if (latestBuffer.Length >= 16 && _echoSender != null)
                {
                    var tickBytes = new byte[4];
                    Buffer.BlockCopy(latestBuffer, 12, tickBytes, 0, 4);
                    _ = _echoSender.SendAsync(tickBytes, 4, echoEndpoint);
                }

                var packet = RevvPacket.FromBytes(latestBuffer);
                LatestSteering    = packet.SteeringValue;
                LatestThrottle    = packet.ThrottleValue;
                LatestBrake       = packet.BrakeValue;
                LatestButtons     = packet.Buttons;
                LatestRightStickX = packet.RightStickX;
                LatestRightStickY = packet.RightStickY;
                LatestLeftStickX  = packet.LeftStickX;
                LatestLeftStickY  = packet.LeftStickY;
                PacketsReceived++;
                _lastPacketTime = DateTime.UtcNow;

                SteeringReceived?.Invoke(this, packet.SteeringValue);
                ThrottleReceived?.Invoke(this, packet.ThrottleValue);
                BrakeReceived?.Invoke(this, packet.BrakeValue);
                ButtonsReceived?.Invoke(this, packet.Buttons);
                RightStickReceived?.Invoke(this, (packet.RightStickX, packet.RightStickY));
                LeftStickReceived?.Invoke(this, (packet.LeftStickX, packet.LeftStickY));
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

        LatestSteering    = 0f;
        LatestThrottle    = 0f;
        LatestBrake       = 0f;
        LatestButtons     = 0;
        LatestRightStickX = 0f;
        LatestRightStickY = 0f;
        LatestLeftStickX  = 0f;
        LatestLeftStickY  = 0f;
        PhoneDisconnected?.Invoke(this, EventArgs.Empty);

        try { _dataListener?.Close(); } catch { }
        _dataListener = null;
        try { _echoSender?.Close(); } catch { }
        _echoSender = null;
        try { _rumbleSender?.Close(); } catch { }
        _rumbleSender = null;

        if (!ct.IsCancellationRequested)
            _ = EnterDiscoveryAsync(ct);
    }

    private void Cleanup()
    {
        try { _discoveryListener?.Close(); } catch { }
        try { _dataListener?.Close(); } catch { }
        try { _echoSender?.Close(); } catch { }
        try { _rumbleSender?.Close(); } catch { }
        _discoveryListener = null;
        _dataListener = null;
        _echoSender = null;
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
