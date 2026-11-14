using Mizu.Core.Devices;
using Mizu.Core.Native;

namespace Mizu.Core.Services;

public sealed record DiscoveryReport(DateTimeOffset Time, IReadOnlyList<Peripheral> Devices, IReadOnlyList<object> HidInterfaces, string[] Notes);

public sealed class BatteryService
{
    private readonly SemaphoreSlim gate = new(1);
    private IReadOnlyList<Peripheral> last = [];
    private readonly string cachePath;
    public BatteryService(string? cachePath = null)
    {
        this.cachePath = Path.GetFullPath(cachePath ?? Path.Combine(SettingsStore.DataDirectory, "battery-cache.json"));
        try
        {
            last = (System.Text.Json.JsonSerializer.Deserialize<Peripheral[]>(File.ReadAllText(this.cachePath)) ?? [])
                .Where(d => d.ObservedAt is { } at && DateTimeOffset.UtcNow - at < TimeSpan.FromHours(24))
                .Select(d => d with { State = LinkState.Offline, Detail = "上次读数，等待设备回复" }).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
    }
    public IReadOnlyList<HidEndpoint> Endpoints { get; private set; } = [];
    public string[] Notes { get; private set; } = [];
    private readonly object stateGate = new();
    private readonly Dictionary<string, IReadOnlyList<Peripheral>> parts = new();
    private readonly SemaphoreSlim atkGate = new(1);
    public IReadOnlyList<BluetoothLink> BluetoothLinks { get; set; } = [];
    public IReadOnlyList<Peripheral> Current { get { lock(stateGate) return Compose(); } }
    private IReadOnlyList<Peripheral> Compose()
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = parts.Values.SelectMany(x => x).ToList();
        foreach (var previous in last)
            if (!candidates.Any(d => d.Id == previous.Id) && previous.ObservedAt is { } at && now >= at && now - at < TimeSpan.FromHours(24))
                candidates.Add(previous with { State = LinkState.Offline, Detail = "上次读数，当前连接未确认" });
        var links = BluetoothLinks;
        candidates = candidates.Select(device => {
            if (device.Link is LinkKind.Unknown or LinkKind.Wired && !string.IsNullOrWhiteSpace(device.ContainerId) &&
                candidates.Any(d => d.ContainerId == device.ContainerId && d.Link == LinkKind.Bluetooth)) device = device with { Link = LinkKind.Bluetooth };
            if (device.Link != LinkKind.Bluetooth) return device;
            var address = device.BluetoothAddress ?? candidates.FirstOrDefault(d => !string.IsNullOrWhiteSpace(device.ContainerId) && d.ContainerId == device.ContainerId && d.BluetoothAddress is not null)?.BluetoothAddress;
            device = device with { BluetoothAddress = address };
            var signal = links.FirstOrDefault(l => l.Matches(device));
            // A disconnect event invalidates current readings immediately. A
            // connect event does not turn an old cached battery report fresh.
            return signal?.Connected == false ? device with { State = LinkState.Offline } : device;
        }).ToList();
        last = Merge(candidates).OrderByDescending(d => d.State == LinkState.Online).ThenBy(d => d.Kind).ToArray();
        return last;
    }
    private void Apply(string part, IReadOnlyList<Peripheral> devices, Action<IReadOnlyList<Peripheral>>? progress)
    { lock(stateGate) { parts[part] = devices; progress?.Invoke(Compose()); } }
    public async Task<IReadOnlyList<Peripheral>> RefreshFastAsync(CancellationToken token, Action<IReadOnlyList<Peripheral>>? progress = null)
    {
        var nodes = WindowsDevices.EnumeratePnp(token); var endpoints = WindowsDevices.EnumerateHid(token);
        Apply("windows", await new WindowsBatteryProvider(nodes, endpoints, BluetoothLinks).ScanAsync(token), progress);
        if (await atkGate.WaitAsync(0, token))
        {
            try { Apply("atk", await new AtkBatteryProvider(endpoints).ScanAsync(token), progress); }
            finally { atkGate.Release(); }
        }
        return Current;
    }
    public async Task<IReadOnlyList<Peripheral>> ScanAsync(CancellationToken token, Action<IReadOnlyList<Peripheral>>? progress = null)
    {
        await gate.WaitAsync(token);
        try
        {
            var notes = new System.Collections.Concurrent.ConcurrentBag<string>();
            var nodes = WindowsDevices.EnumeratePnp(token);
            var endpoints = WindowsDevices.EnumerateHid(token); Endpoints = endpoints;
            Apply("windows", await new WindowsBatteryProvider(nodes, endpoints, BluetoothLinks).ScanAsync(token), progress);
            async Task Run(string key, IBatteryProvider provider, SemaphoreSlim? transportGate = null)
            {
                if (transportGate is not null) await transportGate.WaitAsync(token);
                try { Apply(key, await provider.ScanAsync(token).ConfigureAwait(false), progress); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { notes.Add(provider.Name + ": " + ex.GetType().Name); Apply(key, [], progress); AppLog.Write("battery.provider", provider.Name + " " + ex.GetType().Name); }
                finally { transportGate?.Release(); }
            }
            await Task.WhenAll(Task.Run(() => Run("standard", new StandardHidBatteryProvider(endpoints)), token),
                Run("logitech", new LogitechBatteryProvider(endpoints)), Run("atk", new AtkBatteryProvider(endpoints), atkGate));
            Notes = notes.ToArray(); var current = Current;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.WriteAllText(cachePath + ".tmp", System.Text.Json.JsonSerializer.Serialize(current.Where(d => d.ObservedAt is not null)));
                File.Move(cachePath + ".tmp", cachePath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("battery.cache", ex.GetType().Name); }
            return current;
        }
        finally { gate.Release(); }
    }
    public static List<Peripheral> Merge(IEnumerable<Peripheral> devices) => devices
        .GroupBy(d => !string.IsNullOrEmpty(d.ContainerId) && d.ContainerId != Guid.Empty.ToString() ? "container:" + d.ContainerId + ":" + d.Kind : d.Id)
        .Select(g => g.OrderByDescending(d => d.State == LinkState.Online).ThenByDescending(d => d.Source.Contains("HID++") || d.Source.StartsWith("ATK"))
            .ThenByDescending(d => d.ObservedAt).First()).OrderBy(d => d.Kind).ThenBy(d => d.Name).ToList();
    public DiscoveryReport Report(IReadOnlyList<Peripheral> devices) => new(DateTimeOffset.Now, devices, Endpoints.Select(e => (object)new
    {
        Id = DeviceIdentity.Hash(e.InstanceId), e.VendorId, e.ProductId, e.UsagePage, e.Usage, e.Product,
        e.InputLength, e.OutputLength, e.FeatureLength
    }).ToArray(), Notes);
}
