using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Revv.Shared.Networking;

public class RevvBroadcaster : IAsyncDisposable
{
    public int SendRateHz { get; set; } = 60;

    public BroadcasterState State { get; private set; } = BroadcasterState.Idle;
    public string? ConnectedPcIp { get; private set; }

    public event EventHandler<string>? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<Exception>? ErrorOccurred;

    private UdpClient? _broadcaster;
    private UdpClient? _ackListener;
    private UdpClient? _streamer;
    private CancellationTokenSource? _cts;
    private float _latestSteering = 0f;
    private float _latestThrottle = 0f;
    private float _latestBrake = 0f;
    private readonly object _steeringLock = new();

    public Task StartAsync()
    {
        if (State != BroadcasterState.Idle) return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        _ = EnterDiscoveryAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public void SetSteering(float value)
    {
        lock (_steeringLock)
            _latestSteering = Math.Clamp(value, -1f, 1f);
    }

    public void SetThrottle(float value)
    {
        lock (_steeringLock)
            _latestThrottle = Math.Clamp(value, 0f, 1f);
    }

    public void SetBrake(float value)
    {
        lock (_steeringLock)
            _latestBrake = Math.Clamp(value, 0f, 1f);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        await Task.Delay(100);
        Cleanup();
        State = BroadcasterState.Idle;
    }

    private async Task EnterDiscoveryAsync(CancellationToken ct)
    {
        State = BroadcasterState.Discovering;
        ConnectedPcIp = null;

        try
        {
            _broadcaster = new UdpClient { EnableBroadcast = true };
            _ackListener = new UdpClient(RevvDiscovery.PhoneListenPort);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);
            return;
        }

        var broadcastEp = new IPEndPoint(IPAddress.Broadcast, RevvDiscovery.BroadcastPort);
        var beacon = Encoding.UTF8.GetBytes(RevvDiscovery.BuildBeacon());

        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested && State == BroadcasterState.Discovering)
                {
                    await _broadcaster.SendAsync(beacon, beacon.Length, broadcastEp);
                    await Task.Delay(RevvDiscovery.BroadcastIntervalMs, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ErrorOccurred?.Invoke(this, ex); }
        }, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await _ackListener.ReceiveAsync(ct);
                var message = Encoding.UTF8.GetString(result.Buffer);

                if (message == RevvDiscovery.AckMessage)
                {
                    ConnectedPcIp = result.RemoteEndPoint.Address.ToString();
                    _ackListener.Close();
                    _broadcaster.Close();
                    await EnterStreamingAsync(ConnectedPcIp, ct);
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ErrorOccurred?.Invoke(this, ex); }
    }

    private async Task EnterStreamingAsync(string pcIp, CancellationToken ct)
    {
        State = BroadcasterState.Streaming;
        Connected?.Invoke(this, pcIp);

        _streamer = new UdpClient();
        var pcEndpoint = new IPEndPoint(IPAddress.Parse(pcIp), RevvDiscovery.DataPort);
        var intervalMs = 1000 / SendRateHz;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                float steering, throttle, brake;
                lock (_steeringLock)
                {
                    steering = _latestSteering;
                    throttle = _latestThrottle;
                    brake = _latestBrake;
                }

                var packet = new RevvPacket(steering, throttle, brake).ToBytes();
                await _streamer.SendAsync(packet, packet.Length, pcEndpoint);
                await Task.Delay(intervalMs, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, ex);

            if (!ct.IsCancellationRequested)
            {
                State = BroadcasterState.Idle;
                Disconnected?.Invoke(this, EventArgs.Empty);
                _streamer?.Close();
                await EnterDiscoveryAsync(ct);
            }
        }
    }

    private void Cleanup()
    {
        try { _broadcaster?.Close(); } catch { }
        try { _ackListener?.Close(); } catch { }
        try { _streamer?.Close(); } catch { }
        _broadcaster = null;
        _ackListener = null;
        _streamer = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}

public enum BroadcasterState
{
    Idle,
    Discovering,
    Streaming
}
