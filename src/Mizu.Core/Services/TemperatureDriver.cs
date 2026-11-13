using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Mizu.Core.Services;

public enum DriverInstallState { Installed, AlreadyInstalled, RebootRequired, Failed }
public sealed record DriverInstallResult(DriverInstallState State, int ExitCode);

public static class TemperatureDriver
{
    public const string Version = "2.2.0";
    public const string InstallerSha256 = "1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032";
    public const string ReleaseUrl = "https://github.com/namazso/PawnIO.Setup/releases/tag/2.2.0";
    public static string InstallerPath => Path.Combine(AppContext.BaseDirectory, "drivers", "PawnIO", "PawnIO_setup.exe");
    public static string? UnsupportedReason => GetUnsupportedReason(RuntimeInformation.OSArchitecture, Environment.OSVersion.Version);

    public static string? GetUnsupportedReason(Architecture osArchitecture, System.Version windowsVersion)
    {
        if (osArchitecture != Architecture.X64) return "当前温度读取仅支持 x64 Windows；32 位和 ARM 系统暂不支持";
        if (windowsVersion < new System.Version(10, 0, 17763)) return "温度驱动需要 Windows 10 1809 或更新版本";
        return null;
    }

    // Read on demand: LHM's PawnIo.Version is initialized once per process and
    // cannot detect a driver installed while Mizu is already running.
    public static System.Version? InstalledVersion
    {
        get
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            return System.Version.TryParse(key?.GetValue("DisplayVersion") as string, out var version) ? version : null;
        }
    }

    public static bool MatchesInstallerHash(string hash) => string.Equals(hash, InstallerSha256, StringComparison.OrdinalIgnoreCase);
    public static DriverInstallResult InterpretExitCode(int code) => new(code switch
    {
        0 => DriverInstallState.Installed,
        3010 => DriverInstallState.RebootRequired,
        _ => DriverInstallState.Failed
    }, code);

    public static async Task<DriverInstallResult> InstallAsync()
    {
        if (UnsupportedReason is { } reason) throw new PlatformNotSupportedException(reason);
        if (InstalledVersion is not null) return new(DriverInstallState.AlreadyInstalled, 0);
        // Hold a read-only share throughout execution so the validated executable
        // cannot be overwritten or deleted between verification and elevation.
        using var file = new FileStream(InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(file));
        if (!MatchesInstallerHash(hash)) throw new InvalidDataException("内置驱动安装包校验失败，请重新解压官方 Mizu 便携包");
        // Only reached from the explicit settings button. Use the signed official
        // installer with its default restricted edition; never alter boot security.
        using var process = Process.Start(new ProcessStartInfo(InstallerPath)
        {
            UseShellExecute = true, Verb = "runas", Arguments = "-install -silent",
            WorkingDirectory = Path.GetDirectoryName(InstallerPath), WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new IOException("无法启动温度驱动安装程序");
        // Do not cancel or kill an in-progress driver installation if settings close.
        await process.WaitForExitAsync();
        var result = InterpretExitCode(process.ExitCode);
        if (result.State == DriverInstallState.Installed && InstalledVersion is null)
            throw new IOException("安装程序已结束，但未检测到 PawnIO；请查看系统安装日志");
        return result;
    }
}
