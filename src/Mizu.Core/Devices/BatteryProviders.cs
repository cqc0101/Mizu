using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mizu.Core.Native;
using Mizu.Core.Services;

namespace Mizu.Core.Devices;

public static class DeviceIdentity
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant())))[..24];
    public static string Physical(HidEndpoint e) => string.IsNullOrWhiteSpace(e.ContainerId) || e.ContainerId == Guid.Empty.ToString() ? e.InstanceId : e.ContainerId;
}

public sealed class WindowsBatteryProvider(IReadOnlyList<PnpNode> nodes, IReadOnlyList<HidEndpoint> hid) : IBatteryProvider
{
    public string Name => "Windows battery property";
    public Task<IReadOnlyList<Peripheral>> ScanAsync(CancellationToken token)
    {
        var results = new List<Peripheral>();
        foreach (var n in nodes.Where(x => x.Battery is not null))
        {
            token.ThrowIfCancellationRequested();
            var family = nodes.Where(x => x.ContainerId == n.ContainerId && !string.IsNullOrEmpty(n.ContainerId)).ToArray();
            var bt = n.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase) || family.Any(x => x.InstanceId.StartsWith("BTH", StringComparison.OrdinalIgnoreCase));
            var kinds = family.Select(x => DeviceClassifier.FromMetadata(x.Class)).Where(x => x != PeripheralKind.Unknown).Distinct().ToArray();
            var hidKinds = hid.Where(x => x.ContainerId == n.ContainerId).Select(x => DeviceClassifier.FromHid(x.UsagePage, x.Usage)).Where(x => x != PeripheralKind.Unknown).Distinct().ToArray();
            var kind = hidKinds.Length == 1 ? hidKinds[0] : kinds.Length == 1 ? kinds[0] : DeviceClassifier.FromMetadata(n.Name);
            // Do not label headphones/phones as keyboard or mouse by a model-name substring.
            if (kind == PeripheralKind.Unknown && !(n.Class.Equals("Keyboard", StringComparison.OrdinalIgnoreCase) || n.Class.Equals("Mouse", StringComparison.OrdinalIgnoreCase))) continue;
            var connected = n.Connected ?? family.Select(x => x.Connected).FirstOrDefault(x => x.HasValue);
            var state = connected == true ? LinkState.Online : connected == false ? LinkState.Offline : LinkState.Unknown;
            var id = "win:" + DeviceIdentity.Hash(string.IsNullOrEmpty(n.ContainerId) ? n.InstanceId : n.ContainerId);
            results.Add(new(id, n.Name, kind, bt ? LinkKind.Bluetooth : LinkKind.Unknown, state, n.Battery, null, Name,
                connected == true ? DateTimeOffset.UtcNow : null, n.ContainerId, "Windows 缓存电量；刷新速度由设备与系统决定"));
        }
        return Task.FromResult<IReadOnlyList<Peripheral>>(results.GroupBy(x => x.Id).Select(x => x.OrderByDescending(y => y.State == LinkState.Online).First()).ToArray());
    }
}

public sealed class StandardHidBatteryProvider(IReadOnlyList<HidEndpoint> endpoints) : IBatteryProvider
{
    public string Name => "Standard HID BatteryStrength";
    public Task<IReadOnlyList<Peripheral>> ScanAsync(CancellationToken token)
    {
        var result = new List<Peripheral>();
        foreach (var e in endpoints.Where(x => x.FeatureLength > 0))
        {
            token.ThrowIfCancellationRequested();
            var kind = DeviceClassifier.FromHid(e.UsagePage, e.Usage);
            if (kind == PeripheralKind.Unknown) continue;
            try
            {
                if (WindowsDevices.ReadStandardBattery(e) is not { } percent) continue;
                result.Add(new("hid:" + DeviceIdentity.Hash(DeviceIdentity.Physical(e)), e.Product, kind,
                    e.Path.Contains("bth", StringComparison.OrdinalIgnoreCase) ? LinkKind.Bluetooth : LinkKind.Unknown,
                    LinkState.Online, percent, null, Name, DateTimeOffset.UtcNow, e.ContainerId));
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { AppLog.Write("hid.standard", ex.GetType().Name); }
        }
        return Task.FromResult<IReadOnlyList<Peripheral>>(result.DistinctBy(x => x.Id).ToArray());
    }
}

public sealed class LogitechBatteryProvider(IReadOnlyList<HidEndpoint> endpoints) : IBatteryProvider
{
    public string Name => "Logitech HID++ 2.0";
    public async Task<IReadOnlyList<Peripheral>> ScanAsync(CancellationToken token)
    {
        var result = new List<Peripheral>();
        var candidates = endpoints.Where(x => x.VendorId == 0x046d && x.UsagePage == 0xff00 && x.Usage is 1 or 2 && x.OutputLength >= 20)
            .GroupBy(DeviceIdentity.Physical).Select(g => g.OrderByDescending(x => x.Usage == 2).ThenByDescending(x => x.InputLength).First());
        foreach (var endpoint in candidates)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                await using var channel = new HidppTransport(endpoints.Where(e => e.VendorId == 0x046d && e.UsagePage == 0xff00 && DeviceIdentity.Physical(e) == DeviceIdentity.Physical(endpoint)), token);
                byte software = (byte)Random.Shared.Next(1, 16);
                bool receiverConnecting = false;
                async Task<byte[]?> Query(byte slot, byte feat, byte function, params byte[] args)
                {
                    // Rotate the ID on EVERY transaction so late replies to an earlier getFeature
                    // or name chunk cannot satisfy the next request.
                    software = (byte)(software % 15 + 1);
                    var q = HidppProtocol.Request(slot, feat, function, software, args);
                    var r = await channel.QueryAsync(q, token).ConfigureAwait(false);
                    receiverConnecting = r is { Length: >= 7 } && r[2] == 0x8f && r[5] == 4;
                    return r is null || HidppProtocol.IsError(r) ? null : r;
                }
                async Task<byte> Feature(byte slot, ushort id)
                { var r = await Query(slot, 0, 0, (byte)(id >> 8), (byte)id); return r is { Length: >= 5 } ? r[4] : (byte)0; }

                foreach (byte slot in new byte[] { 1, 2, 3, 4, 5, 6, 0xff })
                {
                    var ping = await Query(slot, 0, 1, 0, 0, 0x5a);
                    if (ping is null && receiverConnecting)
                    {
                        await Task.Delay(220, token).ConfigureAwait(false);
                        ping = await Query(slot, 0, 1, 0, 0, 0x5a);
                    }
                    if (ping is not { Length: >= 7 } || ping[4] < 2 || ping[6] != 0x5a) continue;
                    int? percent = null; bool? charging = null; ushort used = 0;
                    foreach (ushort fid in new ushort[] { 0x1004, 0x1000 })
                    {
                        var fi = await Feature(slot, fid);
                        if (fi == 0) continue;
                        var r = await Query(slot, fi, fid == 0x1004 ? (byte)1 : (byte)0);
                        if (r is null) continue;
                        (percent, charging) = HidppProtocol.Battery(r, fid); used = fid;
                        if (percent is not null) break;
                    }
                    if (used == 0) continue; // Receiver itself is not a battery-powered peripheral.
                    var name = endpoint.Product.Length > 0 && slot == 0xff ? endpoint.Product : "Logitech";
                    var kind = PeripheralKind.Unknown;
                    var nameFeature = await Feature(slot, 5);
                    if (nameFeature != 0)
                    {
                        var type = await Query(slot, nameFeature, 2);
                        if (type?.Length >= 5) kind = DeviceClassifier.FromLogitech(type[4]);
                        var lengthReply = await Query(slot, nameFeature, 0);
                        if (lengthReply is { Length: >= 5 } && lengthReply[4] is > 0 and <= 128)
                        {
                            var nameLength = lengthReply[4]; var raw = new List<byte>();
                            for (byte offset = 0; offset < nameLength; offset += 16)
                            {
                                var chunk = await Query(slot, nameFeature, 1, offset);
                                if (chunk is null) break;
                                raw.AddRange(chunk.Skip(4).Take(Math.Min(16, nameLength - offset)));
                            }
                            if (raw.Count == nameLength) name = Encoding.UTF8.GetString(raw.ToArray()).TrimEnd('\0');
                        }
                    }
                    if (kind == PeripheralKind.Unknown) kind = DeviceClassifier.FromMetadata(name);
                    var bt = endpoint.Path.Contains("bth", StringComparison.OrdinalIgnoreCase);
                    result.Add(new("logi:" + DeviceIdentity.Hash(DeviceIdentity.Physical(endpoint)) + ":" + slot, name, kind,
                        bt ? LinkKind.Bluetooth : slot == 0xff ? LinkKind.Unknown : LinkKind.Receiver,
                        LinkState.Online, percent, charging, $"{Name} / 0x{used:X4}", DateTimeOffset.UtcNow,
                        slot == 0xff ? endpoint.ContainerId : null));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            { AppLog.Write("hid.logitech", ex.GetType().Name); }
        }
        return result;
    }
}

public sealed record AtkModel(int VendorId, int ProductId, int UsagePage, int Usage, string Name, int? CompanyId, int? ModelId);

public sealed class AtkBatteryProvider(IReadOnlyList<HidEndpoint> endpoints) : IBatteryProvider
{
    public string Name => "ATK / VXE";
    private static readonly Lazy<AtkModel[]> Catalog = new(() =>
    {
        try { return JsonSerializer.Deserialize<AtkModel[]>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "atk-catalog.json"))) ?? []; }
        catch (Exception ex) when (ex is IOException or JsonException) { AppLog.Write("atk.catalog", ex.GetType().Name); return []; }
    });
    public async Task<IReadOnlyList<Peripheral>> ScanAsync(CancellationToken token)
    {
        var result = new List<Peripheral>();
        foreach (var identified in endpoints.Where(e => Catalog.Value.Any(m => m.VendorId == e.VendorId && m.ProductId == e.ProductId && m.UsagePage == e.UsagePage && m.Usage == e.Usage))
            .DistinctBy(e => DeviceIdentity.Physical(e)))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var e = identified;
                var own = Catalog.Value.First(m => m.ProductId == identified.ProductId && m.UsagePage == identified.UsagePage && m.Usage == identified.Usage);
                if (e.UsagePage == 0xff04 && (e.FeatureLength < 17 || e.InputLength < 17))
                {
                    // Chromium groups collections of one HID interface together; native
                    // Windows handles do not. Older Compx receivers carry report 8 on
                    // their FF02 sibling while the catalog filter names FF04.
                    e = endpoints.FirstOrDefault(s => s.VendorId == e.VendorId && s.ProductId == e.ProductId
                        && DeviceIdentity.Physical(s) == DeviceIdentity.Physical(e) && s.UsagePage == 0xff02 && s.Usage == 2
                        && s.InputLength == 17 && s.OutputLength == 17) ?? e;
                }
                using var channel = new HidChannel(e);
                string name = e.Product.Length > 0 ? e.Product : "ATK";
                int? percent = null; bool? charging = null; LinkKind link = LinkKind.Unknown;
                if (e.UsagePage is 0xff04 or 0xff02)
                {
                    async Task<byte[]?> Read(byte command) => await channel.ExchangeAsync(AtkProtocol.CompxRequest(command), r => AtkProtocol.MatchCompx(r, command), token, e.FeatureLength >= 17);
                    var online = await Read(3);
                    if (online is null || online[6] != 1) continue;
                    var r = await Read(4);
                    if (r is not { Length: >= 8 } || r[6] > 100) continue;
                    percent = r[6]; charging = r[7] == 1;
                    var id = await Read(0x10);
                    var model = id is { Length: >= 8 } ? Catalog.Value.FirstOrDefault(m => m.UsagePage is 0xff04 or 0xff02 && m.CompanyId == id[6] && m.ModelId == id[7]) : null;
                    name = model?.Name ?? own.Name;
                    link = own.Name.Contains("dongle", StringComparison.OrdinalIgnoreCase) ? LinkKind.Receiver : LinkKind.Wired;
                }
                else if (e.UsagePage == 0xff05)
                {
                    async Task<byte[]?> Read(byte command, byte target)
                    { var q = AtkProtocol.BitMouseRequest(command, target); return await channel.ExchangeAsync(q, r => AtkProtocol.MatchBitMouse(r, q), token); }
                    byte target = own.Name.Contains("dongle", StringComparison.OrdinalIgnoreCase) ? (byte)1 : (byte)0;
                    if (target == 1)
                    { var online = await Read(0x81, 0); if (online is null || online[8] != 1) continue; }
                    var r = await Read(7, target);
                    if (r is null || r[8] > 100) continue;
                    percent = r[8]; var cr = await Read(0x0f, target); charging = cr is null ? null : cr[8] == 1;
                    var connection = await Read(0x1a, 0);
                    link = connection is null ? LinkKind.Unknown : connection[8] is 0 or 1 or 4 or 5 ? LinkKind.Receiver : connection[8] is 2 or 3 or 6 or 7 ? LinkKind.Wired : LinkKind.Unknown;
                    var id = await Read(target == 1 ? (byte)0x89 : (byte)0x4a, target);
                    if (id is null || id[9] == 0) id = await Read(0x4a, target);
                    var model = id is not null ? Catalog.Value.FirstOrDefault(m => m.UsagePage == 0xff05 && m.CompanyId == id[9] && m.ModelId == BitConverter.ToUInt32(id, 10)) : null;
                    name = model?.Name ?? own.Name;
                    // NearLink is a separate radio, not Bluetooth or ordinary 2.4G.
                    if (link == LinkKind.Receiver && (name.Contains("NK") || own.Name.Contains("NK"))) link = LinkKind.NearLink;
                }
                if (percent is not null) result.Add(new("atk:" + DeviceIdentity.Hash(DeviceIdentity.Physical(e)), name, PeripheralKind.Mouse, link,
                    LinkState.Online, percent, charging, Name + (e.UsagePage is 0xff04 or 0xff02 ? " Compx" : " BitMouse"), DateTimeOffset.UtcNow, e.ContainerId,
                    "协议适配完成，具体固件仍需实机验证"));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            { AppLog.Write("hid.atk", ex.GetType().Name); }
        }
        return result;
    }
}
