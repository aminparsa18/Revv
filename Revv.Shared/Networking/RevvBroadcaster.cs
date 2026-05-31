using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Revv.Shared.Networking;

public class RevvBroadcaster : IAsyncDisposable
{
    public int SendRateHz { get; set; } = 120;

    public BroadcasterState State { get; private set; } = BroadcasterState.Idle;
    public string? ConnectedPcIp { get; private set; }

    public event EventHandler<string>? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<Exception>? ErrorOccurred;
    public event EventHandler<int>? LatencyUpdated; // smoothed RTT in ms
    public event EventHandler<(byte Large, byte Small)>? RumbleReceived;

    private UdpClient? _broadcaster;
    private UdpClient? _ackListener;
    private UdpClient? _streamer;
    private UdpClient? _echoListener;
    private UdpClient? _rumbleListener;
    private CancellationTokenSource? _cts;
    private float  _latestSteering     = 0f;
    private float  _latestThrottle     = 0f;
    private float  _latestBrake        = 0f;
    private ushort _latestButtons      = 0;
    private float  _latestRightStickX  = 0f;
    private float  _latestRightStickY  = 0f;
    private float  _latestLeftStickX   = 0f;
    private float  _latestLeftStickY   = 0f;
    private readonly object _steeringLock = new();
    private float _smoothedLatencyMs = -1f;
    private long _lastLatencyFireMs = 0;
    private long _lastEchoTimeMs = 0;
    private const float LatencyEmaAlpha = 0.2f;
    private const int EchoTimeoutMs = 3000;

    public Task StartAsync()
    {
        // Cancel any in-flight session first (handles OnDisappearing/OnAppearing overlap).
        var oldCts = _cts;
        oldCts?.Cancel();
        _cts = new CancellationTokenSource();
        Cleanup();
        State = BroadcasterState.Idle;

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

    public void SetRightStick(float x, float y)
    {
        lock (_steeringLock)
        {
            _latestRightStickX = Math.Clamp(x, -1f, 1f);
            _latestRightStickY = Math.Clamp(y, -1f, 1f);
        }
    }

    public void SetLeftStick(float x, float y)
    {
        lock (_steeringLock)
        {
            _latestLeftStickX = Math.Clamp(x, -1f, 1f);
            _latestLeftStickY = Math.Clamp(y, -1f, 1f);
        }
    }

    public void SetButton(RevvButtonMask button, bool pressed)
    {
        lock (_steeringLock)
        {
            if (pressed)
                _latestButtons |= (ushort)button;
            else
                _latestButtons &= (ushort)~(int)button;
        }
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        cts?.Cancel();
        await Task.Delay(100);
        // Only finalize cleanup if StartAsync hasn't already replaced the session.
        if (ReferenceEquals(_cts, cts))
        {
            Cleanup();
            State = BroadcasterState.Idle;
        }
    }

    private async Task EnterDiscoveryAsync(CancellationToken ct)
    {
        State = BroadcasterState.Discovering;
        ConnectedPcIp = null;

        try
        {
            _broadcaster = new UdpClient { EnableBroadcast = true };
            _ackListener = BindWithReuseAddr(RevvDiscovery.PhoneListenPort);
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
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                    ErrorOccurred?.Invoke(this, ex);
            }
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
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                ErrorOccurred?.Invoke(this, ex);
        }
    }

    private async Task EnterStreamingAsync(string pcIp, CancellationToken ct)
    {
        State = BroadcasterState.Streaming;

        // Clear any button bits that got stuck during discovery or a previous session.
        lock (_steeringLock) _latestButtons = 0;

        Connected?.Invoke(this, pcIp);

        _streamer = new UdpClient();
        _smoothedLatencyMs = -1f;
        _lastEchoTimeMs = 0;

        try
        {
            _echoListener = BindWithReuseAddr(RevvDiscovery.EchoPort);
            _ = ReceiveEchoesAsync(_echoListener, ct);
        }
        catch { /* echo unavailable — latency display stays at "—" */ }

        try
        {
            _rumbleListener = BindWithReuseAddr(RevvDiscovery.RumblePort);
            _ = ReceiveRumbleAsync(_rumbleListener, ct);
        }
        catch { /* rumble unavailable */ }

        var pcEndpoint = new IPEndPoint(IPAddress.Parse(pcIp), RevvDiscovery.DataPort);
        var intervalMs = 1000 / SendRateHz;

        bool lostConnection = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                float steering, throttle, brake, rightX, rightY, leftX, leftY;
                ushort buttons;
                lock (_steeringLock)
                {
                    steering = _latestSteering;
                    throttle = _latestThrottle;
                    brake    = _latestBrake;
                    buttons  = _latestButtons;
                    rightX   = _latestRightStickX;
                    rightY   = _latestRightStickY;
                    leftX    = _latestLeftStickX;
                    leftY    = _latestLeftStickY;
                }

                var tick = (uint)(Environment.TickCount64 & 0xFFFFFFFFL);
                var packet = new RevvPacket(steering, throttle, brake, tick, buttons, rightX, rightY, leftX, leftY).ToBytes();
                await _streamer.SendAsync(packet, packet.Length, pcEndpoint);
                await Task.Delay(intervalMs, ct);

                // Watchdog: if the PC stopped echoing, treat it as a disconnect.
                // Only active once the first echo has been received (_lastEchoTimeMs > 0),
                // so we never false-positive before the PC has had a chance to respond.
                if (_echoListener != null && _lastEchoTimeMs > 0 &&
                    Environment.TickCount64 - _lastEchoTimeMs > EchoTimeoutMs)
                {
                    lostConnection = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            lostConnection = !ct.IsCancellationRequested;
            if (lostConnection)
                ErrorOccurred?.Invoke(this, ex);
        }

        if (lostConnection)
        {
            State = BroadcasterState.Idle;
            Disconnected?.Invoke(this, EventArgs.Empty);
            _streamer?.Close();
            _echoListener?.Close();
            _echoListener = null;
            _rumbleListener?.Close();
            _rumbleListener = null;
            await EnterDiscoveryAsync(ct);
        }
    }

    private async Task ReceiveRumbleAsync(UdpClient rumbleListener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await rumbleListener.ReceiveAsync(ct);
                if (result.Buffer.Length < 2) continue;
                RumbleReceived?.Invoke(this, (result.Buffer[0], result.Buffer[1]));
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }

    private async Task ReceiveEchoesAsync(UdpClient echoListener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await echoListener.ReceiveAsync(ct);
                if (result.Buffer.Length < 4) continue;

                var sentTick = BitConverter.ToUInt32(result.Buffer, 0);
                var nowTick = (uint)(Environment.TickCount64 & 0xFFFFFFFFL);
                var rawRtt = (int)(nowTick - sentTick);

                if (rawRtt < 0 || rawRtt > 2000) continue;

                _lastEchoTimeMs = Environment.TickCount64;

                _smoothedLatencyMs = _smoothedLatencyMs < 0
                    ? rawRtt
                    : LatencyEmaAlpha * rawRtt + (1f - LatencyEmaAlpha) * _smoothedLatencyMs;

                var now = Environment.TickCount64;
                if (now - _lastLatencyFireMs >= 500)
                {
                    _lastLatencyFireMs = now;
                    LatencyUpdated?.Invoke(this, (int)MathF.Round(_smoothedLatencyMs));
                }
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }

    private void Cleanup()
    {
        try { _broadcaster?.Close(); } catch { }
        try { _ackListener?.Close(); } catch { }
        try { _streamer?.Close(); } catch { }
        try { _echoListener?.Close(); } catch { }
        try { _rumbleListener?.Close(); } catch { }
        _broadcaster = null;
        _ackListener = null;
        _streamer = null;
        _echoListener = null;
        _rumbleListener = null;
    }

    private static UdpClient BindWithReuseAddr(int port)
    {
        var client = new UdpClient();
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port));
        return client;
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
