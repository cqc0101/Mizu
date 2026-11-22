using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Mizu.Core.Native;
using LibreHardwareMonitor.Hardware;

namespace Mizu.Core.Services;

public sealed record GpuMemorySnapshot(long? DedicatedBytes, long? SharedBytes, long? DedicatedCapacityBytes, string Source)
{
    public long? SharedCapacityBytes { get; init; }
    public double? VramCelsius { get; init; }
    public string? VramTemperatureSource { get; init; }
}

public sealed class GpuMemoryReader : IDisposable
{
    private const uint MoreData = 0x800007D2;
    private const uint FormatLargeUnscaled = 0x400 | 0x1000;
    private const long MiB = 1024 * 1024;
    private IntPtr query;
    private readonly record struct Counter(IntPtr Handle, GpuAdapterCapacity.AdapterLuid? Luid);
    private Counter[] dedicated = [], shared = [];
    private readonly Dictionary<GpuAdapterCapacity.AdapterLuid, (long Dedicated, long Shared)?> capacities = new();
    private DateTimeOffset nextWindowsAttempt;
    private string? nvidiaSmi;
    private bool checkedNvidiaPath;
    private bool nvidiaMemoryTemperatureUnsupported;
    private DateTimeOffset nextNvidiaTemperatureAttempt;
    private Computer? gpuSensors;
    private DateTimeOffset nextSensorAttempt;

    public Task<GpuMemorySnapshot?> SampleAsync(CancellationToken cancellationToken) => SampleAsync(true, false, cancellationToken);
    public async Task<GpuMemorySnapshot?> SampleAsync(bool readUsage, bool readTemperature, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!readUsage) CloseWindowsQuery();
        if (!readTemperature) CloseSensors();
        var result = readUsage ? SampleWindows() : null;
        var temperature = readTemperature ? ReadVramTemperature() : null;
        bool usageFallback = readUsage && result is null;
        bool queryTemperature = readTemperature && temperature is null && !nvidiaMemoryTemperatureUnsupported &&
            DateTimeOffset.UtcNow >= nextNvidiaTemperatureAttempt;
        if (usageFallback || queryTemperature)
        {
            if (!checkedNvidiaPath)
            {
                checkedNvidiaPath = true;
                nvidiaSmi = FindNvidiaSmi();
            }
            if (nvidiaSmi is { } path)
            {
                var output = await RunNvidiaAsync(path, queryTemperature, cancellationToken);
                if (output is null && queryTemperature && usageFallback)
                {
                    output = await RunNvidiaAsync(path, false, cancellationToken);
                    if (output is not null) nvidiaMemoryTemperatureUnsupported = true;
                }
                var nvidia = output is null ? null : ParseNvidia(output, queryTemperature);
                if (queryTemperature && nvidia?.VramCelsius is null) nextNvidiaTemperatureAttempt = DateTimeOffset.UtcNow.AddSeconds(30);
                if (nvidia?.VramCelsius is { } vram) temperature = (vram, nvidia.VramTemperatureSource!);
                if (usageFallback) result = nvidia;
            }
        }
        if (temperature is { } sensor)
            result = (result ?? new(null, null, null, "")) with { VramCelsius = sensor.Value, VramTemperatureSource = sensor.Source };
        return readUsage || result is null ? result : result with { DedicatedBytes = null, SharedBytes = null, DedicatedCapacityBytes = null, SharedCapacityBytes = null };
    }

    internal static bool IsVramTemperature(string name, SensorType type) => type == SensorType.Temperature &&
        (name.Equals("GPU Memory Junction", StringComparison.OrdinalIgnoreCase) || name.Equals("GPU Memory", StringComparison.OrdinalIgnoreCase));

    private (double Value, string Source)? ReadVramTemperature()
    {
        if (gpuSensors is null && DateTimeOffset.UtcNow < nextSensorAttempt) return null;
        try
        {
            if (gpuSensors is null)
            {
                gpuSensors = new Computer { IsGpuEnabled = true };
                gpuSensors.Open();
            }
            (double Value, string Source)? hottest = null;
            foreach (var card in gpuSensors.Hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd))
            {
                try
                {
                    card.Update();
                    var reading = card.Sensors.Where(s => IsVramTemperature(s.Name, s.SensorType) && s.Value is > 0 and <= 150)
                        .OrderByDescending(s => s.Name.Equals("GPU Memory Junction", StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(s => s.Value).FirstOrDefault();
                    if (reading?.Value is { } value && (hottest is null || value > hottest.Value.Value))
                        hottest = (value, card.Name + " · " + reading.Name);
                }
                catch (Exception ex) { AppLog.Write("vram.sensor", ex.GetType().Name); }
            }
            return hottest;
        }
        catch (Exception ex)
        {
            AppLog.Write("vram.temperature", ex.GetType().Name);
            CloseSensors();
            nextSensorAttempt = DateTimeOffset.UtcNow.AddMinutes(1);
            return null;
        }
    }

    private void CloseSensors()
    {
        if (gpuSensors is null) return;
        try { gpuSensors.Close(); } catch (Exception ex) { AppLog.Write("vram.close", ex.GetType().Name); }
        gpuSensors = null;
    }

    private GpuMemorySnapshot? SampleWindows()
    {
        var now = DateTimeOffset.UtcNow;
        if (query != IntPtr.Zero && now >= nextWindowsAttempt) CloseWindowsQuery();
        if (query == IntPtr.Zero)
        {
            if (now < nextWindowsAttempt) return null;
            nextWindowsAttempt = now.AddMinutes(1);
            if (PdhOpenQueryW(null, UIntPtr.Zero, out var handle) != 0) return null;
            query = handle;
            dedicated = OpenCounters(@"\GPU Adapter Memory(*)\Dedicated Usage");
            shared = OpenCounters(@"\GPU Adapter Memory(*)\Shared Usage");
        }
        if (PdhCollectQueryData(query) == 0)
        {
            var local = ReadTotal(dedicated, sharedPool: false);
            var system = ReadTotal(shared, sharedPool: true);
            if (local.Used is not null || system.Used is not null)
                return new(local.Used, system.Used, local.Capacity, "Windows · 已读取适配器合计") { SharedCapacityBytes = system.Capacity };
        }
        CloseWindowsQuery();
        nextWindowsAttempt = DateTimeOffset.UtcNow.AddMinutes(1);
        return null;
    }

    private Counter[] OpenCounters(string englishPath)
    {
        if (PdhAddEnglishCounterW(query, englishPath, UIntPtr.Zero, out var handle) != 0) return [];
        try
        {
            uint bytes = 0;
            if (PdhGetCounterInfoW(handle, false, ref bytes, IntPtr.Zero) != MoreData ||
                bytes < Marshal.SizeOf<CounterInfo>() || bytes > 1024 * 1024) return [];
            var buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                if (PdhGetCounterInfoW(handle, false, ref bytes, buffer) != 0) return [];
                var path = Marshal.PtrToStringUni(Marshal.PtrToStructure<CounterInfo>(buffer).FullPath);
                return path is null ? [] : ExpandCounters(path);
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { PdhRemoveCounter(handle); }
    }

    private Counter[] ExpandCounters(string path)
    {
        uint chars = 0;
        if (PdhExpandWildCardPathW(null, path, null, ref chars, 0) != MoreData || chars == 0 || chars > 262144) return [];
        var paths = new char[chars];
        if (PdhExpandWildCardPathW(null, path, paths, ref chars, 0) != 0) return [];
        var result = new List<Counter>();
        foreach (var expanded in new string(paths).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (expanded.Contains(@"(_Total)\", StringComparison.OrdinalIgnoreCase)) continue;
            if (PdhAddCounterW(query, expanded, UIntPtr.Zero, out var counter) == 0) result.Add(new(counter, GpuAdapterCapacity.FromCounterPath(expanded)));
        }
        return result.ToArray();
    }

    private (long? Used, long? Capacity) ReadTotal(Counter[] counters, bool sharedPool)
    {
        long? used = null;
        bool known = true;
        var adapters = new HashSet<GpuAdapterCapacity.AdapterLuid>();
        foreach (var counter in counters)
        {
            if (PdhGetFormattedCounterValue(counter.Handle, FormatLargeUnscaled, IntPtr.Zero, out var value) == 0 &&
                value.Status <= 1 && value.LargeValue >= 0)
            {
                if (value.LargeValue > long.MaxValue - used.GetValueOrDefault()) return (null, null);
                used = used.GetValueOrDefault() + value.LargeValue;
                if (counter.Luid is { } luid) adapters.Add(luid);
                else known = false;
            }
        }
        // Shared RAM limits can refer to the same physical pool on several GPUs.
        if (!known || adapters.Count == 0 || (sharedPool && adapters.Count != 1)) return (used, null);
        long total = 0;
        foreach (var adapter in adapters)
        {
            if (!capacities.TryGetValue(adapter, out var capacity)) capacities[adapter] = capacity = GpuAdapterCapacity.Read(adapter);
            if (capacity is not { } amount) return (used, null);
            long bytes = sharedPool ? amount.Shared : amount.Dedicated;
            if (bytes > long.MaxValue - total) return (used, null);
            total += bytes;
        }
        return (used, total > 0 ? total : null);
    }

    private static string? FindNvidiaSmi()
    {
        var nativeSystem = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative") : Environment.SystemDirectory;
        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432") ??
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return new[] { Path.Combine(nativeSystem, "nvidia-smi.exe"),
            Path.Combine(programFiles, "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe") }.FirstOrDefault(File.Exists);
    }

    private static async Task<string?> RunNvidiaAsync(string path, bool temperature, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var process = new Process
        {
            StartInfo = new(path, "--query-gpu=memory.used,memory.total" + (temperature ? ",temperature.memory" : "") + " --format=csv,noheader,nounits")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }
        };
        try
        {
            if (!process.Start()) return null;
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return process.ExitCode == 0 ? output : null;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException) { return null; }
    }

    internal static GpuMemorySnapshot? ParseNvidia(string output, bool readTemperature)
    {
        long used = 0, capacity = 0;
        double? temperature = null;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length is < 2 or > 3) continue;
            if (long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rowUsed) &&
                long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rowCapacity) &&
                rowCapacity > 0 && rowUsed <= rowCapacity && rowCapacity <= long.MaxValue / MiB - capacity)
            {
                used += rowUsed;
                capacity += rowCapacity;
            }
            if (readTemperature && fields.Length == 3 && double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value is > 0 and <= 150)
                temperature = Math.Max(temperature ?? 0, value);
        }
        return capacity > 0 || temperature is not null
            ? new(capacity > 0 ? used * MiB : null, null, capacity > 0 ? capacity * MiB : null, "NVIDIA · 仅 NVIDIA 适配器")
                { VramCelsius = temperature, VramTemperatureSource = temperature is null ? null : "NVIDIA · 显存温度" }
            : null;
    }

    private void CloseWindowsQuery()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
        query = IntPtr.Zero;
        dedicated = shared = [];
        capacities.Clear();
    }

    public void Dispose()
    {
        CloseWindowsQuery();
        CloseSensors();
        nextWindowsAttempt = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue { public uint Status; public long LargeValue; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterInfo
    {
        public uint Length, Type, Version, Status;
        public int Scale, DefaultScale;
        public UIntPtr UserData, QueryUserData;
        public IntPtr FullPath;
    }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhOpenQueryW(string? dataSource, UIntPtr userData, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddCounterW(IntPtr query, string path, UIntPtr userData, out IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetCounterInfoW(IntPtr counter, [MarshalAs(UnmanagedType.Bool)] bool explainText, ref uint size, IntPtr buffer);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhExpandWildCardPathW(string? dataSource, string path, [Out] char[]? paths, ref uint size, uint flags);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhRemoveCounter(IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out CounterValue value);
    [DllImport("pdh.dll", ExactSpelling = true)]
    private static extern uint PdhCloseQuery(IntPtr query);
}
