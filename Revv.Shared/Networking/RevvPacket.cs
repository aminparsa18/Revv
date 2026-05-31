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
    public const int SizeBytes = 34;

    public float  SteeringValue { get; }
    public float  ThrottleValue { get; }
    public float  BrakeValue    { get; }
    public uint   SendTickMs    { get; }
    public ushort Buttons       { get; }
    public float  RightStickX  { get; }
    public float  RightStickY  { get; }
    public float  LeftStickX   { get; }
    public float  LeftStickY   { get; }

    public RevvPacket(float steering, float throttle = 0f, float brake = 0f, uint sendTickMs = 0,
                      ushort buttons = 0, float rightStickX = 0f, float rightStickY = 0f,
                      float leftStickX = 0f, float leftStickY = 0f)
    {
        SteeringValue = Math.Clamp(steering,    -1f, 1f);
        ThrottleValue = Math.Clamp(throttle,     0f, 1f);
        BrakeValue    = Math.Clamp(brake,        0f, 1f);
        SendTickMs    = sendTickMs;
        Buttons       = buttons;
        RightStickX  = Math.Clamp(rightStickX, -1f, 1f);
        RightStickY  = Math.Clamp(rightStickY, -1f, 1f);
        LeftStickX   = Math.Clamp(leftStickX,  -1f, 1f);
        LeftStickY   = Math.Clamp(leftStickY,  -1f, 1f);
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[SizeBytes];
        BitConverter.GetBytes(SteeringValue).CopyTo(bytes,  0);
        BitConverter.GetBytes(ThrottleValue).CopyTo(bytes,  4);
        BitConverter.GetBytes(BrakeValue).CopyTo(bytes,     8);
        BitConverter.GetBytes(SendTickMs).CopyTo(bytes,    12);
        BitConverter.GetBytes(Buttons).CopyTo(bytes,       16);
        BitConverter.GetBytes(RightStickX).CopyTo(bytes,  18);
        BitConverter.GetBytes(RightStickY).CopyTo(bytes,  22);
        BitConverter.GetBytes(LeftStickX).CopyTo(bytes,   26);
        BitConverter.GetBytes(LeftStickY).CopyTo(bytes,   30);
        return bytes;
    }

    public static RevvPacket FromBytes(byte[] data)
    {
        if (data.Length < 4)
            throw new ArgumentException($"Packet too short: expected at least 4 bytes, got {data.Length}.");

        return new RevvPacket(
            BitConverter.ToSingle(data, 0),
            data.Length >= 8  ? BitConverter.ToSingle(data,  4) : 0f,
            data.Length >= 12 ? BitConverter.ToSingle(data,  8) : 0f,
            data.Length >= 16 ? BitConverter.ToUInt32(data, 12) : 0u,
            data.Length >= 18 ? BitConverter.ToUInt16(data, 16) : (ushort)0,
            data.Length >= 22 ? BitConverter.ToSingle(data, 18) : 0f,
            data.Length >= 26 ? BitConverter.ToSingle(data, 22) : 0f,
            data.Length >= 30 ? BitConverter.ToSingle(data, 26) : 0f,
            data.Length >= 34 ? BitConverter.ToSingle(data, 30) : 0f
        );
    }

    public override string ToString() =>
        $"S:{SteeringValue:F3} T:{ThrottleValue:F3} B:{BrakeValue:F3} Btn:{Buttons:X} RS:({RightStickX:F2},{RightStickY:F2}) LS:({LeftStickX:F2},{LeftStickY:F2})";
}
