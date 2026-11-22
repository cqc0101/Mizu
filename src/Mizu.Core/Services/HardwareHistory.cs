namespace Mizu.Core.Services;

public enum HardwareMetric { Cpu, Memory, CpuTemperature, GpuDedicated, GpuShared }
public readonly record struct HardwareSample(DateTimeOffset Time, double? Value, bool StartsSegment = false);

// Owned by the UI dispatcher. Bounded, session-only storage; no extra hardware polling.
public sealed class HardwareHistory
{
    public static readonly TimeSpan Retention = TimeSpan.FromHours(1);
    public const int MaximumSamplesPerMetric = 1801;
    private sealed class Series
    {
        public readonly Queue<HardwareSample> Samples = new();
        public DateTimeOffset? Last;
        public bool BreakBeforeNext = true;
    }
    private readonly Dictionary<HardwareMetric, Series> series = new();
    public event Action? Changed;

    public void RecordSystem(DateTimeOffset time, SystemSnapshot snapshot, AppSettings settings)
    {
        ApplySettings(settings);
        Prune(time);
        if (settings.RecordHardwareHistory && MonitoringEnabled(settings)) {
            Append(HardwareMetric.Cpu, time, settings.ShowCpu ? Percent(snapshot.CpuPercent) : null);
            Append(HardwareMetric.Memory, time, settings.ShowMemory ? Percent(snapshot.MemoryPercent) : null);
            Append(HardwareMetric.CpuTemperature, time, settings.ShowTemperature && settings.TemperatureEnabled ? snapshot.CpuCelsius : null);
        }
        // The time window keeps advancing while recording is paused. Reuse the
        // existing system tick, with no timer or polling added for history.
        Changed?.Invoke();
    }

    public void RecordGpu(DateTimeOffset time, GpuMemorySnapshot? snapshot, AppSettings settings)
    {
        ApplySettings(settings);
        Prune(time);
        if (!settings.RecordHardwareHistory || !settings.ShowGpuMemory) return;
        Append(HardwareMetric.GpuDedicated, time, snapshot?.DedicatedBytes is >= 0 ? snapshot.DedicatedBytes : null);
        Append(HardwareMetric.GpuShared, time, snapshot?.SharedBytes is >= 0 ? snapshot.SharedBytes : null);
        Changed?.Invoke();
    }

    public static bool MonitoringEnabled(AppSettings settings) =>
        settings.ShowCpu || settings.ShowMemory || settings.ShowTemperature || settings.ShowGpuMemory;

    public void ApplySettings(AppSettings settings)
    {
        foreach (var entry in series) {
            bool enabled = entry.Key switch {
                HardwareMetric.Cpu => settings.ShowCpu,
                HardwareMetric.Memory => settings.ShowMemory,
                HardwareMetric.CpuTemperature => settings.ShowTemperature && settings.TemperatureEnabled,
                _ => settings.ShowGpuMemory
            };
            if (!settings.RecordHardwareHistory || !enabled) entry.Value.BreakBeforeNext = true;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var data in series.Values)
            while (data.Samples.TryPeek(out var first) && first.Time < now - Retention)
                data.Samples.Dequeue();
    }

    private static double? Percent(double? value) => value is >= 0 and <= 100 ? value : null;
    private void Append(HardwareMetric metric, DateTimeOffset time, double? value)
    {
        if (value is { } v && !double.IsFinite(v)) value = null;
        if (!series.TryGetValue(metric, out var data)) series[metric] = data = new();
        // A corrected system clock must not leave future readings in a new curve.
        if (data.Last > time) data.Samples.Clear();
        else if (data.Last == time) return;
        data.Last = time;
        data.Samples.Enqueue(new(time, value, data.BreakBeforeNext));
        data.BreakBeforeNext = value is null;
        while (data.Samples.Count > MaximumSamplesPerMetric ||
               data.Samples.TryPeek(out var first) && first.Time < time - Retention)
            data.Samples.Dequeue();
    }

    public IReadOnlyList<HardwareSample> Read(HardwareMetric metric, DateTimeOffset now, TimeSpan range)
    {
        if (!series.TryGetValue(metric, out var data)) return [];
        var start = now - (range < Retention ? range : Retention);
        return data.Samples.Where(s => s.Time >= start && s.Time <= now).ToArray();
    }
}
