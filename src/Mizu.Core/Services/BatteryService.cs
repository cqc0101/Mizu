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
    public async Task<IReadOnlyList<Peripheral>> ScanAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            var notes = new List<string>();
            var nodes = WindowsDevices.EnumeratePnp(token);
            Endpoints = WindowsDevices.EnumerateHid(token);
            IBatteryProvider[] providers = [new WindowsBatteryProvider(nodes, Endpoints), new StandardHidBatteryProvider(Endpoints),
                new LogitechBatteryProvider(Endpoints), new AtkBatteryProvider(Endpoints)];
            var found = new List<Peripheral>();
            foreach (var provider in providers)
            {
                token.ThrowIfCancellationRequested();
                try { found.AddRange(await provider.ScanAsync(token).ConfigureAwait(false)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { notes.Add(provider.Name + ": " + ex.GetType().Name); AppLog.Write("battery.provider", provider.Name + " " + ex.GetType().Name); }
            }
            // Merge only when a physical container proves identity. A receiver's slots
            // deliberately have no container ID here; a receiver may contain six devices.
            var merged = Merge(found);
            foreach (var previous in last)
                if (!merged.Any(d => d.Id == previous.Id) && previous.ObservedAt is { } at && DateTimeOffset.UtcNow - at < TimeSpan.FromHours(24))
                    merged.Add(previous with { State = LinkState.Offline, Detail = "暂时未收到设备回复；保留上次读数" });
            last = merged.OrderByDescending(d => d.State == LinkState.Online).ThenByDescending(d => d.ObservedAt.HasValue).ThenBy(d => d.Kind).ToArray(); Notes = notes.ToArray();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                File.WriteAllText(cachePath + ".tmp", System.Text.Json.JsonSerializer.Serialize(last.Where(d => d.ObservedAt is not null)));
                File.Move(cachePath + ".tmp", cachePath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("battery.cache", ex.GetType().Name); }
            return last;
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
