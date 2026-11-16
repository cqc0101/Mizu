namespace Mizu.Core;

public sealed class PeripheralPresence
{
    private readonly Dictionary<string, (Peripheral Device, DateTimeOffset Signal)> history = [];
    private HashSet<string> present = [];
    public IReadOnlyList<Peripheral> Update(IEnumerable<Peripheral> devices, DateTimeOffset now)
    {
        var snapshot = devices.ToArray();
        foreach (var device in snapshot)
        {
            bool known = history.TryGetValue(device.Id, out var previous);
            DateTimeOffset? signal = device.ObservedAt is { } observed && observed <= now ? observed : null;
            if (signal is null && device.State == LinkState.Online && (!known || previous.Device.State != LinkState.Online || !present.Contains(device.Id))) signal = now;
            if (known && (signal is null || signal < previous.Signal)) signal = previous.Signal;
            if (signal is { } last) history[device.Id] = (device, last);
        }
        present = snapshot.Select(d => d.Id).ToHashSet();
        // Keep expired timestamps until new evidence arrives: repeating a cached
        // Online record with no timestamp must not restart its grace period.
        return history.Values.Where(item => now >= item.Signal && now - item.Signal < TimeSpan.FromHours(1))
            .Select(item => item.Device with { DisplayDimmed = now - item.Signal >= TimeSpan.FromMinutes(15) }).ToArray();
    }
}
