namespace Revv.Shared.Networking;

[Flags]
public enum RevvButtonMask : ushort
{
    None  = 0,
    A     = 1 << 0,
    B     = 1 << 1,
    X     = 1 << 2,
    Y     = 1 << 3,
    Start = 1 << 4,
}

public readonly struct RevvPacket
{
    public const int SizeBytes = 18;

    public float SteeringValue { get; }
    public float ThrottleValue { get; }
    public float BrakeValue    { get; }
    public uint  SendTickMs    { get; }
    public ushort Buttons      { get; }

    public RevvPacket(float steering, float throttle = 0f, float brake = 0f, uint sendTickMs = 0, ushort buttons = 0)
    {
        SteeringValue = Math.Clamp(steering, -1f, 1f);
        ThrottleValue = Math.Clamp(throttle, 0f, 1f);
        BrakeValue    = Math.Clamp(brake,    0f, 1f);
        SendTickMs    = sendTickMs;
        Buttons       = buttons;
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[SizeBytes];
        BitConverter.GetBytes(SteeringValue).CopyTo(bytes, 0);
        BitConverter.GetBytes(ThrottleValue).CopyTo(bytes, 4);
        BitConverter.GetBytes(BrakeValue).CopyTo(bytes, 8);
        BitConverter.GetBytes(SendTickMs).CopyTo(bytes, 12);
        BitConverter.GetBytes(Buttons).CopyTo(bytes, 16);
        return bytes;
    }

    public static RevvPacket FromBytes(byte[] data)
    {
        if (data.Length < 4)
            throw new ArgumentException($"Packet too short: expected at least 4 bytes, got {data.Length}.");

        return new RevvPacket(
            BitConverter.ToSingle(data, 0),
            data.Length >= 8  ? BitConverter.ToSingle(data, 4)  : 0f,
            data.Length >= 12 ? BitConverter.ToSingle(data, 8)  : 0f,
            data.Length >= 16 ? BitConverter.ToUInt32(data, 12) : 0u,
            data.Length >= 18 ? BitConverter.ToUInt16(data, 16) : (ushort)0
        );
    }

    public override string ToString() => $"S:{SteeringValue:F3} T:{ThrottleValue:F3} B:{BrakeValue:F3} Btn:{Buttons:X}";
}
