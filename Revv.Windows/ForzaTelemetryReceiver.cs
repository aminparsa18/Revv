using System.Diagnostics;
using System.Net.Sockets;

namespace Revv.Windows;

// Receives Forza Horizon UDP telemetry and extracts vehicle speed.
// In-game: Settings → HUD and Gameplay → Data Out → On
//          Data Out IP   = this PC's LAN IP
//          Data Out Port = 9999
//
// Receives Forza Horizon 6 UDP telemetry and extracts vehicle speed.
// FH6 sends a single 324-byte packet while actively driving (not in menus/paused).
// In-game: Settings → HUD and Gameplay → Data Out → On
//          Data Out IP   = 127.0.0.1  (loopback — game and app are on the same PC)
//          Data Out Port = 9999
//
// Packet layout (official FH6 docs):
//   Offset 256 — F32 Speed (m/s)  ← velocity field, not position
//   Offset 244 — F32 PositionX    ← NOT speed (common mistake with FM format)
public class ForzaTelemetryReceiver : IAsyncDisposable
{
    public const int SpeedPort = 20066;

    private const int Fh6PacketSize = 324;
    private const int Fh6SpeedOffset = 256;

    public event EventHandler<float>? SpeedUpdated;

    public bool  IsListening        { get; private set; }
    public int   RawPacketsReceived { get; private set; }  // every packet, any size
    public int   PacketsReceived    { get; private set; }  // parsed successfully
    public int   LastPacketSize     { get; private set; }
    public float CurrentSpeedMs     => Environment.TickCount64 - _lastPacketMs < 2000 ? _speedMs : 0f;
    public float CurrentSpeedKmh    => CurrentSpeedMs * 3.6f;

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private volatile float _speedMs = 0f;
    private long _lastPacketMs = 0;

    public Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        try
        {
            TryAddFirewallRule();
            _udp = new UdpClient(SpeedPort);
            IsListening = true;
            _ = ReceiveLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ForzaTelemetry] Failed to bind port {SpeedPort}: {ex.Message}");
            IsListening = false;
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        try { _udp?.Close(); } catch { }
        await Task.Delay(50);
        IsListening = false;
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udp!.ReceiveAsync(ct);
                var buf = result.Buffer;
                RawPacketsReceived++;
                LastPacketSize = buf.Length;

                float speed = buf.Length >= Fh6PacketSize
                    ? BitConverter.ToSingle(buf, Fh6SpeedOffset)
                    : -1f;

                if (RawPacketsReceived <= 5)
                    Debug.WriteLine($"[ForzaTelemetry] Packet #{RawPacketsReceived}: {buf.Length} bytes, speed={speed:F2} m/s");

                if (speed < 0f) continue;

                _speedMs = speed;
                _lastPacketMs = Environment.TickCount64;
                PacketsReceived++;
                SpeedUpdated?.Invoke(this, _speedMs);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Debug.WriteLine($"[ForzaTelemetry] Receive error: {ex.Message}"); }
        }
    }

    // Adds a Windows Firewall inbound rule for the telemetry port.
    // Runs silently — if it fails (no admin) the user can add it manually.
    private static void TryAddFirewallRule()
    {
        try
        {
            var args = $"advfirewall firewall add rule " +
                       $"name=\"Revv Forza Telemetry\" " +
                       $"dir=in action=allow protocol=UDP localport={SpeedPort} " +
                       $"enable=yes profile=any";

            Process.Start(new ProcessStartInfo("netsh", args)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "runas"
            });
        }
        catch { /* no admin — user must add rule manually */ }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
