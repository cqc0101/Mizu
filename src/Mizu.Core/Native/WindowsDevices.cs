using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Mizu.Core.Native;

public sealed record PnpNode(string InstanceId, string Name, string Class, string ContainerId,
    string ParentId, int? Battery, bool? Connected);
public sealed record HidEndpoint(string Path, ushort VendorId, ushort ProductId, ushort UsagePage,
    ushort Usage, int InputLength, int OutputLength, int FeatureLength, string Product, string ContainerId, string InstanceId);

public static class WindowsDevices
{
    [StructLayout(LayoutKind.Sequential)] internal struct DeviceInfo
    { public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] internal struct InterfaceInfo
    { public uint Size; public Guid ClassGuid; public uint Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] internal struct PropertyKey
    { public Guid Format; public uint Id; public PropertyKey(string guid, uint id) { Format = new(guid); Id = id; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Attributes
    { public uint Size; public ushort Vendor, Product, Version; }
    [StructLayout(LayoutKind.Sequential)] internal struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort LinkNodes, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues,
            OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    private const string DeviceFormat = "a45c254e-df1c-4efd-8020-67d146a850e0";
    private static byte[]? Property(IntPtr set, ref DeviceInfo info, PropertyKey key, out uint type)
    {
        var data = new byte[8192];
        if (!SetupDiGetDevicePropertyW(set, ref info, ref key, out type, data, (uint)data.Length, out var size, 0)) return null;
        return data[..(int)size];
    }
    private static string StringProperty(IntPtr set, ref DeviceInfo info, string guid, uint id)
    { var data = Property(set, ref info, new(guid, id), out var type); return data is not null && (type == 0x12 || type == 0x2012) ? Encoding.Unicode.GetString(data).TrimEnd('\0').Replace('\0', ',') : ""; }
    private static string Container(IntPtr set, ref DeviceInfo info)
    { var data = Property(set, ref info, new("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2), out var type); return type == 0x0d && data?.Length == 16 ? new Guid(data).ToString() : ""; }
    private static string Instance(IntPtr set, ref DeviceInfo info)
    { var text = new StringBuilder(1024); return SetupDiGetDeviceInstanceIdW(set, ref info, text, text.Capacity, out _) ? text.ToString() : ""; }

    public static IReadOnlyList<PnpNode> EnumeratePnp(CancellationToken token)
    {
        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, 4); // Include paired, currently disconnected devices.
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = new List<PnpNode>();
        try
        {
            for (uint i = 0; ; i++)
            {
                token.ThrowIfCancellationRequested();
                var info = new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() };
                if (!SetupDiEnumDeviceInfo(set, i, ref info)) break;
                var id = Instance(set, ref info);
                // Bluetooth parents and HID children are sufficient; avoid querying every PCI/storage node.
                if (!(id.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) || id.StartsWith("HID", StringComparison.OrdinalIgnoreCase))) continue;
                var name = StringProperty(set, ref info, DeviceFormat, 14);
                if (name.Length == 0) name = StringProperty(set, ref info, DeviceFormat, 2);
                var raw = Property(set, ref info, new("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 2), out var type);
                int? battery = raw is null ? null : type switch
                { 3 when raw.Length >= 1 => raw[0], 5 when raw.Length >= 2 => BitConverter.ToUInt16(raw), 7 when raw.Length >= 4 => (int)BitConverter.ToUInt32(raw), _ => null };
                if (battery is < 0 or > 100) battery = null;
                var connection = Property(set, ref info, new("83da6326-97a6-4088-9453-a1923f573b29", 15), out var ct);
                bool? connected = connection?.Length > 0 && ct == 0x11 ? connection[0] != 0 : null;
                result.Add(new(id, name, StringProperty(set, ref info, DeviceFormat, 9), Container(set, ref info),
                    StringProperty(set, ref info, "4340a6c5-93fa-4706-972c-7b648008a5a7", 8), battery, connected));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    public static IReadOnlyList<HidEndpoint> EnumerateHid(CancellationToken token)
    {
        HidD_GetHidGuid(out var hidGuid);
        var ptr = Marshal.AllocHGlobal(16);
        Marshal.StructureToPtr(hidGuid, ptr, false);
        var set = SetupDiGetClassDevsW(ptr, null, IntPtr.Zero, 0x12);
        Marshal.FreeHGlobal(ptr);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var result = new List<HidEndpoint>();
        try
        {
            for (uint i = 0; ; i++)
            {
                token.ThrowIfCancellationRequested();
                var iface = new InterfaceInfo { Size = (uint)Marshal.SizeOf<InterfaceInfo>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref iface)) break;
                SetupDiGetDeviceInterfaceDetailW(set, ref iface, IntPtr.Zero, 0, out var needed, IntPtr.Zero);
                if (needed < 6 || needed > 32768) continue;
                var detail = Marshal.AllocHGlobal((int)needed);
                var devPtr = Marshal.AllocHGlobal(Marshal.SizeOf<DeviceInfo>());
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    Marshal.StructureToPtr(new DeviceInfo { Size = (uint)Marshal.SizeOf<DeviceInfo>() }, devPtr, false);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref iface, detail, needed, out _, devPtr)) continue;
                    var path = Marshal.PtrToStringUni(detail + 4)!;
                    using var handle = Open(path, false);
                    if (handle.IsInvalid) continue;
                    var attributes = new Attributes { Size = (uint)Marshal.SizeOf<Attributes>() };
                    if (!HidD_GetAttributes(handle, ref attributes) || !HidD_GetPreparsedData(handle, out var pp)) continue;
                    try
                    {
                        if (HidP_GetCaps(pp, out var caps) != 0x110000) continue;
                        var product = new byte[512];
                        var name = HidD_GetProductString(handle, product, product.Length) ? Encoding.Unicode.GetString(product).TrimEnd('\0') : "";
                        var info = Marshal.PtrToStructure<DeviceInfo>(devPtr);
                        result.Add(new(path, attributes.Vendor, attributes.Product, caps.UsagePage, caps.Usage,
                            caps.InputLength, caps.OutputLength, caps.FeatureLength, name, Container(set, ref info), Instance(set, ref info)));
                    }
                    finally { HidD_FreePreparsedData(pp); }
                }
                finally { Marshal.FreeHGlobal(detail); Marshal.FreeHGlobal(devPtr); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    internal static SafeFileHandle Open(string path, bool readWrite) => CreateFileW(path, readWrite ? 0xc0000000u : 0,
        3, IntPtr.Zero, 3, readWrite ? 0x40000000u : 0, IntPtr.Zero);

    public static int? ReadStandardBattery(HidEndpoint endpoint)
    {
        using var handle = Open(endpoint.Path, false);
        if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out var pp)) return null;
        try
        {
            if (HidP_GetCaps(pp, out var caps) != 0x110000 || caps.FeatureValues == 0 || caps.FeatureLength == 0) return null;
            ushort count = caps.FeatureValues;
            var data = new byte[count * 72];
            if (HidP_GetValueCaps(2, data, ref count, pp) != 0x110000) return null;
            for (var i = 0; i < count; i++)
            {
                var offset = i * 72;
                var page = BitConverter.ToUInt16(data, offset);
                var isRange = data[offset + 12] != 0;
                var min = BitConverter.ToUInt16(data, offset + 56);
                var max = isRange ? BitConverter.ToUInt16(data, offset + 58) : min;
                // Only the standard BatteryStrength usage. Never guess a proprietary feature report.
                if (page != 6 || min > 0x20 || max < 0x20) continue;
                var report = new byte[caps.FeatureLength]; report[0] = data[offset + 2];
                if (!HidD_GetFeature(handle, report, report.Length)) continue;
                if (HidP_GetUsageValue(2, 6, BitConverter.ToUInt16(data, offset + 6), 0x20, out var value, pp, report, (uint)report.Length) != 0x110000) continue;
                var logicalMin = BitConverter.ToInt32(data, offset + 40);
                var logicalMax = BitConverter.ToInt32(data, offset + 44);
                if (logicalMin < 0 || logicalMax <= logicalMin || value < logicalMin || value > logicalMax) continue;
                return (int)Math.Round((value - logicalMin) * 100.0 / (logicalMax - logicalMin));
            }
            return null;
        }
        finally { HidD_FreePreparsedData(pp); }
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevsW(IntPtr guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfo info);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DeviceInfo info, StringBuilder text, int length, out uint required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref DeviceInfo info, ref PropertyKey key, out uint type, byte[] data, uint length, out uint needed, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref InterfaceInfo iface);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref InterfaceInfo iface, IntPtr detail, uint size, out uint needed, IntPtr info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] internal static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] internal static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr pp, out Caps caps);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll")] internal static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
    [DllImport("hid.dll")] static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] report, int length);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int reportType, byte[] caps, ref ushort count, IntPtr pp);
    [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int reportType, ushort page, ushort link, ushort usage, out uint value, IntPtr pp, byte[] report, uint length);
}

public sealed class HidChannel : IDisposable
{
    private readonly FileStream stream;
    private readonly HidEndpoint endpoint;
    public HidChannel(HidEndpoint endpoint)
    {
        this.endpoint = endpoint;
        var handle = WindowsDevices.Open(endpoint.Path, true);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        // HID packets must reach the kernel immediately. A buffered FileStream can
        // retain a short write while the independent response reader waits forever.
        stream = new(handle, FileAccess.ReadWrite, 1, true);
    }
    public async Task<byte[]?> ExchangeAsync(byte[] request, Func<byte[], bool> matches, CancellationToken token, bool feature = false, int timeoutMs = 450)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeoutMs);
        var ct = deadline.Token;
        try
        {
            var length = feature ? endpoint.FeatureLength : endpoint.OutputLength;
            if (length < request.Length || endpoint.InputLength <= 0) return null;
            var report = new byte[length]; request.CopyTo(report, 0);
            if (feature)
            { if (!WindowsDevices.HidD_SetFeature(stream.SafeFileHandle, report, length)) return null; }
            else await stream.WriteAsync(report, ct).ConfigureAwait(false);
            while (!ct.IsCancellationRequested)
            {
                var buffer = new byte[Math.Max(endpoint.InputLength, 20)];
                var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0) return null;
                var response = buffer[..read];
                if (matches(response)) return response;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        return null;
    }
    public async Task SendAsync(byte[] request, CancellationToken token)
    {
        if (request.Length > endpoint.OutputLength) throw new IOException("Report exceeds HID output capacity.");
        var buffer = new byte[endpoint.OutputLength]; request.CopyTo(buffer, 0);
        await stream.WriteAsync(buffer, token).ConfigureAwait(false);
    }
    public async Task<byte[]> ReceiveAsync(CancellationToken token)
    {
        var buffer = new byte[Math.Max(endpoint.InputLength, 7)];
        var length = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
        if (length == 0) throw new EndOfStreamException(); return buffer[..length];
    }
    public void Dispose() => stream.Dispose();
}
