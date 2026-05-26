namespace Revv.Shared.Networking;

public static class RevvDiscovery
{
    /// <summary>Phone broadcasts to this port. PC listens here.</summary>
    public const int BroadcastPort = 5554;

    /// <summary>Phone listens on this port for ACK from PC.</summary>
    public const int PhoneListenPort = 5555;

    /// <summary>PC listens on this port for steering data packets.</summary>
    public const int DataPort = 5556;

    /// <summary>Phone listens on this port for RTT echo replies from the PC.</summary>
    public const int EchoPort = 5557;

    /// <summary>Phone listens on this port for rumble/haptic commands from the PC.</summary>
    public const int RumblePort = 5558;

    /// <summary>How often the phone re-broadcasts while waiting for a PC. (ms)</summary>
    public const int BroadcastIntervalMs = 1000;

    /// <summary>How long the phone waits for an ACK before giving up. (ms)</summary>
    public const int DiscoveryTimeoutMs = 15_000;

    public const string AckMessage = "REVV_ACK";

    public static string BuildBeacon(int dataPort = DataPort) => $"REVV_HERE:{dataPort}";

    public static bool TryParseBeacon(string message, out int dataPort)
    {
        dataPort = 0;
        if (!message.StartsWith("REVV_HERE:")) return false;

        var portStr = message["REVV_HERE:".Length..];
        return int.TryParse(portStr, out dataPort);
    }
}
