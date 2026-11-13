using System.Runtime.InteropServices;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace Mizu.Core.Services;

public sealed class TelemetryService : IDisposable
{
    private ulong previousIdle, previousTotal;
    private Computer? computer;
    private bool temperatureAttempted;
    private bool disposed;
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended; }
    [StructLayout(LayoutKind.Sequential)] private struct FileTime
    { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    public static bool IsAdministrator()
    { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }

    public SystemSnapshot Sample(bool temperatureEnabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        double? cpu = null, memory = null, celsius = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong total = kernel.Value + user.Value;
            if (previousTotal != 0 && total > previousTotal && idle.Value >= previousIdle)
                cpu = Math.Clamp(100 * (1.0 - (double)(idle.Value - previousIdle) / (total - previousTotal)), 0, 100);
            previousTotal = total; previousIdle = idle.Value;
        }
        var ms = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref ms) && ms.TotalPhysical > 0) memory = 100.0 * (ms.TotalPhysical - ms.AvailablePhysical) / ms.TotalPhysical;
        string status = "在设置中启用 CPU 温度读取";
        if (!temperatureEnabled && computer is not null) { computer.Close(); computer = null; temperatureAttempted = false; }
        if (temperatureEnabled)
        {
            if (TemperatureDriver.UnsupportedReason is { } reason) status = reason;
            else if (!IsAdministrator()) status = "CPU 温度通常需要管理员权限；可从设置手动重启";
            else if (!LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled) status = "请在设置中安装内置温度驱动；刚安装完成时需重启 Mizu";
            else
            {
                try
                {
                    if (!temperatureAttempted)
                    {
                        temperatureAttempted = true;
                        computer = new Computer { IsCpuEnabled = true }; computer.Open();
                    }
                    var sensors = new List<(string Name, float Value)>();
                    if (computer is not null)
                        foreach (var hardware in computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu)) Update(hardware, sensors);
                    // Prefer package/Tctl/Tdie. Do not substitute motherboard/ACPI zone temperature.
                    var package = sensors.Where(x => x.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) || x.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)).ToArray();
                    var chosen = package.Length > 0 ? package : sensors.ToArray();
                    if (chosen.Length > 0) celsius = chosen.Max(x => (double)x.Value);
                    status = celsius is null ? "CPU 传感器未提供读数，或驱动被系统阻止" : "CPU 封装温度；无封装读数时使用最高核心温度";
                }
                catch (Exception ex) { status = "CPU 温度不可用"; AppLog.Write("temperature", ex.GetType().Name); }
            }
        }
        return new(cpu, memory, celsius, status);
    }
    private static void Update(IHardware hardware, List<(string, float)> result)
    {
        hardware.Update();
        result.AddRange(hardware.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value is >= -10 and <= 125).Select(s => (s.Name, s.Value!.Value)));
        foreach (var child in hardware.SubHardware) Update(child, result);
    }
    public void Dispose() { if (disposed) return; disposed = true; computer?.Close(); }
}
