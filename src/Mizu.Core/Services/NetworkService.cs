using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace Mizu.Core.Services;

public sealed record NetworkProbeSnapshot(string Target, double? Loss, long? Latency, int Probes);
public sealed record NetworkSnapshot(bool Connected, string Adapter, double? Download, double? Upload, double? Loss, long? Latency, int Probes, string Target)
{ public NetworkProbeSnapshot[] Targets { get; init; } = []; }

public sealed class LossWindow
{
    private readonly Queue<bool> samples = new();
    public int Count => samples.Count;
    public double? Percent => samples.Count == 0 ? null : samples.Count(x => !x) * 100.0 / samples.Count;
    public void Add(bool success) { samples.Enqueue(success); while (samples.Count > 60) samples.Dequeue(); }
    public void Clear() => samples.Clear();
}

public sealed class NetworkService
{
    private string? adapterId;
    private long received, sent, timestamp;
    private readonly Dictionary<string, LossWindow> losses = new(StringComparer.OrdinalIgnoreCase);
    public void Reset() { adapterId = null; timestamp = 0; losses.Clear(); }
    public static string? NormalizeTarget(string? text) {
        if (!IPAddress.TryParse(text?.Trim(), out var address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast) || address.IsIPv6Multicast) return null;
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && address.GetAddressBytes()[0] >= 224) return null;
        return address.ToString();
    }
    public static double? Rate(long previous, long current, double seconds) => seconds <= 0 || current < previous ? null : (current - previous) / seconds;
    public Task<NetworkSnapshot> SampleAsync(string probeTarget, CancellationToken cancel) => SampleAsync(new[] { probeTarget }, cancel);
    public async Task<NetworkSnapshot> SampleAsync(IReadOnlyList<string> probeTargets, CancellationToken cancel)
    {
        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
            .Where(n => n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any)))
            .OrderByDescending(n => n.Id == adapterId).ThenByDescending(n => n.Speed).FirstOrDefault();
        var targets = probeTargets.Select(NormalizeTarget).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        if (adapter is null) { Reset(); return new(false, "", null, null, null, null, 0, targets.FirstOrDefault() ?? "") { Targets = targets.Select(t => new NetworkProbeSnapshot(t,null,null,0)).ToArray() }; }
        if (adapter.Id != adapterId) { Reset(); adapterId = adapter.Id; }
        foreach (var removed in losses.Keys.Except(targets, StringComparer.OrdinalIgnoreCase).ToArray()) losses.Remove(removed);
        foreach (var address in targets) if (!losses.ContainsKey(address)) losses[address] = new();
        var stats = adapter.GetIPStatistics(); var now = Stopwatch.GetTimestamp();
        double? down = timestamp == 0 ? null : Rate(received, stats.BytesReceived, Stopwatch.GetElapsedTime(timestamp, now).TotalSeconds);
        double? up = timestamp == 0 ? null : Rate(sent, stats.BytesSent, Stopwatch.GetElapsedTime(timestamp, now).TotalSeconds);
        received = stats.BytesReceived; sent = stats.BytesSent; timestamp = now;
        using var parallel = new SemaphoreSlim(4);
        var results = await Task.WhenAll(targets.Select(async address => {
            await parallel.WaitAsync(cancel); var loss = losses[address]; long? latency = null;
            try {
                using var ping = new Ping();
                var result = await ping.SendPingAsync(IPAddress.Parse(address), TimeSpan.FromMilliseconds(1200), new byte[16], null, cancel);
                loss.Add(result.Status == IPStatus.Success); if (result.Status == IPStatus.Success) latency = result.RoundtripTime;
            } catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is PingException or System.Net.Sockets.SocketException or InvalidOperationException) { loss.Add(false); }
            finally { parallel.Release(); }
            return new NetworkProbeSnapshot(address, loss.Percent, latency, loss.Count);
        }));
        var first = results.FirstOrDefault();
        return new(true, adapter.Name, down, up, first?.Loss, first?.Latency, first?.Probes ?? 0, first?.Target ?? "") { Targets = results };
    }
}
