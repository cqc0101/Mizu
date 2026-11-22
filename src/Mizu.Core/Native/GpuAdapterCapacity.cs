using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Mizu.Core.Native;

internal static class GpuAdapterCapacity
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct AdapterLuid(uint Low, uint High);
    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapter { public AdapterLuid Luid; public uint Handle; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Query { public uint Handle, Type; public IntPtr Data; public uint Size; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter { public uint Handle; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Segments { public ulong DedicatedVideo, DedicatedSystem, SharedSystem; }
    private const uint GetSegmentSize = 3;

    public static AdapterLuid? FromCounterPath(string path)
    {
        var match = Regex.Match(path, @"\(luid_0x([0-9a-f]{1,8})_0x([0-9a-f]{1,8})_[^)]*\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        return new(uint.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static unsafe (long Dedicated, long Shared)? Read(AdapterLuid luid)
    {
        var adapter = new OpenAdapter { Luid = luid };
        if (D3DKMTOpenAdapterFromLuid(ref adapter) != 0) return null;
        try
        {
            var segments = new Segments();
            var query = new Query { Handle = adapter.Handle, Type = GetSegmentSize, Data = (IntPtr)(&segments), Size = (uint)sizeof(Segments) };
            if (D3DKMTQueryAdapterInfo(ref query) != 0 || segments.DedicatedVideo > long.MaxValue || segments.SharedSystem > long.MaxValue) return null;
            return ((long)segments.DedicatedVideo, (long)segments.SharedSystem);
        }
        finally { var close = new CloseAdapter { Handle = adapter.Handle }; D3DKMTCloseAdapter(ref close); }
    }

    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int D3DKMTQueryAdapterInfo(ref Query query);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int D3DKMTCloseAdapter(ref CloseAdapter adapter);
}
