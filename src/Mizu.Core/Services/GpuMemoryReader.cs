using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Mizu.Core.Services;

public sealed record GpuMemorySnapshot(long? DedicatedBytes, long? SharedBytes, long? DedicatedCapacityBytes, string Source);

public sealed class GpuMemoryReader : IDisposable
{
    private const uint MoreData = 0x800007D2;
    private const uint FormatLargeUnscaled = 0x400 | 0x1000;
    private const long MiB = 1024 * 1024;
    private IntPtr query;
    private IntPtr[] dedicated = [], shared = [];
    private DateTimeOffset nextWindowsAttempt;
    private string? nvidiaSmi;
    private bool checkedNvidiaPath;

    public async Task<GpuMemorySnapshot?> SampleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SampleWindows() is { } windows) return windows;

        if (!checkedNvidiaPath)
        {
            checkedNvidiaPath = true;
            nvidiaSmi = FindNvidiaSmi();
        }
        return nvidiaSmi is null ? null : await SampleNvidiaAsync(nvidiaSmi, cancellationToken);
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
            var dedicatedBytes = ReadTotal(dedicated);
            var sharedBytes = ReadTotal(shared);
            if (dedicatedBytes is not null || sharedBytes is not null)
                return new(dedicatedBytes, sharedBytes, null, "Windows · 所有适配器合计");
        }
        CloseWindowsQuery();
        nextWindowsAttempt = DateTimeOffset.UtcNow.AddMinutes(1);
        return null;
    }

    private IntPtr[] OpenCounters(string englishPath)
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

    private IntPtr[] ExpandCounters(string path)
    {
        uint chars = 0;
        if (PdhExpandWildCardPathW(null, path, null, ref chars, 0) != MoreData || chars == 0 || chars > 262144) return [];
        var paths = new char[chars];
        if (PdhExpandWildCardPathW(null, path, paths, ref chars, 0) != 0) return [];
        var result = new List<IntPtr>();
        foreach (var expanded in new string(paths).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (expanded.Contains(@"(_Total)\", StringComparison.OrdinalIgnoreCase)) continue;
            if (PdhAddCounterW(query, expanded, UIntPtr.Zero, out var counter) == 0) result.Add(counter);
        }
        return result.ToArray();
    }

    private static long? ReadTotal(IntPtr[] counters)
    {
        long? total = null;
        foreach (var counter in counters)
        {
            if (PdhGetFormattedCounterValue(counter, FormatLargeUnscaled, IntPtr.Zero, out var value) == 0 &&
                value.Status <= 1 && value.LargeValue >= 0)
                total = total.GetValueOrDefault() + value.LargeValue;
        }
        return total;
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

    private static async Task<GpuMemorySnapshot?> SampleNvidiaAsync(string path, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var process = new Process
        {
            StartInfo = new(path, "--query-gpu=memory.used,memory.total --format=csv,noheader,nounits")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }
        };
        try
        {
            if (!process.Start()) return null;
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0) return null;
            long used = 0, capacity = 0;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split(',', StringSplitOptions.TrimEntries);
                if (fields.Length != 2 || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var rowUsed) ||
                    !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var rowCapacity) ||
                    rowCapacity == 0 || rowUsed > rowCapacity) continue;
                used += rowUsed;
                capacity += rowCapacity;
            }
            return capacity > 0 ? new(used * MiB, null, capacity * MiB, "NVIDIA · 仅 NVIDIA 适配器") : null;
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

    private void CloseWindowsQuery()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
        query = IntPtr.Zero;
        dedicated = shared = [];
    }

    public void Dispose()
    {
        CloseWindowsQuery();
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
