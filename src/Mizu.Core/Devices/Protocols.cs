namespace Mizu.Core.Devices;

public static class HidppProtocol
{
    public static byte[] Request(byte slot, byte feature, byte function, byte softwareId, params byte[] args)
    {
        if (args.Length > 16 || function > 15 || softwareId is 0 or > 15) throw new ArgumentOutOfRangeException(nameof(args));
        var packet = new byte[20];
        packet[0] = 0x11; packet[1] = slot; packet[2] = feature;
        packet[3] = (byte)((function << 4) | softwareId); args.CopyTo(packet, 4); return packet;
    }
    public static bool Matches(byte[] r, byte[] q)
    {
        if (!ValidFrame(r) || r[1] != q[1]) return false;
        if (r[2] is 0x8f or 0xff) return r[3] == q[2] && r[4] == q[3];
        return r[2] == q[2] && r[3] == q[3];
    }
    private static bool ValidFrame(byte[] r) => r.Length >= 7 && (r[0] == 0x10 || r[0] == 0x11 && r.Length >= 20);
    public static bool IsError(byte[] r) => !ValidFrame(r) || r[2] is 0x8f or 0xff;
    public static (int? Percent, bool? Charging) Battery(byte[] r, ushort feature)
    {
        if (feature is not (0x1004 or 0x1000) || IsError(r) || r[4] > 100) return (null, null);
        // Some devices return 0 for an unavailable percentage and a coarse level in the next byte.
        // Keep that unknown, rather than pretending the coarse level is an exact percentage.
        int? percent = r[4] == 0 && r[5] > 0 ? null : r[4];
        byte status = r[6];
        bool? charging = status switch { 0 => false, 1 or 2 or 4 => true, 3 => false, _ => null };
        return (percent, charging);
    }
}

public static class AtkProtocol
{
    public static byte[] CompxRequest(byte command)
    {
        if (command is not (3 or 4 or 0x10)) throw new ArgumentException("Only read commands are allowed.");
        var b = new byte[17]; b[0] = 8; b[1] = command;
        b[16] = unchecked((byte)(0x55 - b.Take(16).Sum(x => x))); return b;
    }
    public static bool MatchCompx(byte[] r, byte command) => r.Length >= 17 && r[0] == 8 && r[1] == command && r[2] == 0
        && r[3] == 0 && r[4] == 0 && r[5] <= 10 && (r.Take(17).Sum(x => x) & 255) == 0x55;
    public static byte[] BitMouseRequest(byte command, byte target)
    {
        if (command is not (7 or 0x0f or 0x1a or 0x81 or 0x4a or 0x89)) throw new ArgumentException("Only read commands are allowed.");
        var b = new byte[64]; b[0] = 8; b[2] = 0x72;
        b[3] = command switch { 0x81 => 3, 0x4a or 0x89 => 8, _ => 2 };
        b[5] = target; b[6] = command; b[7] = command is 0x4a or 0x89 ? (byte)6 : (byte)1;
        b[1] = unchecked((byte)b.Skip(2).Sum(x => x)); return b;
    }
    public static bool MatchBitMouse(byte[] r, byte[] q) => r.Length >= 64 && r[0] == 8 && r[2] == 0x72 && r[3] != 0xff
        && r[5] == q[5] && r[6] == q[6] && (r.Skip(2).Take(62).Sum(x => x) & 255) == r[1];
}
