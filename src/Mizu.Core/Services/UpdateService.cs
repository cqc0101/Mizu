using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Mizu.Core.Services;

public sealed record ReleaseVersion(int Major, int Minor, int Patch, string Preview) : IComparable<ReleaseVersion>
{
    public string Core => $"{Major}.{Minor}.{Patch}";
    public static ReleaseVersion? Parse(string text)
    {
        if (text.Length > 100) return null;
        var m = Regex.Match(text, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*))?(?:\+[0-9A-Za-z.-]+)?$");
        if (m.Success && m.Groups[4].Value.Split('.').Any(s => s.Length > 1 && s[0] == '0' && s.All(char.IsAsciiDigit))) return null;
        return m.Success && int.TryParse(m.Groups[1].Value, out int major) && int.TryParse(m.Groups[2].Value, out int minor) &&
            int.TryParse(m.Groups[3].Value, out int patch) ? new(major, minor, patch, m.Groups[4].Value) : null;
    }
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        int result = Major.CompareTo(other.Major); if (result != 0) return result;
        result = Minor.CompareTo(other.Minor); if (result != 0) return result;
        result = Patch.CompareTo(other.Patch); if (result != 0) return result;
        if (Preview.Length == 0 || other.Preview.Length == 0)
            return Preview.Length == other.Preview.Length ? 0 : Preview.Length == 0 ? 1 : -1;
        var a = Preview.Split('.'); var b = other.Preview.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) {
            bool an = a[i].All(char.IsAsciiDigit), bn = b[i].All(char.IsAsciiDigit);
            result = an && bn ? a[i].Length != b[i].Length ? a[i].Length.CompareTo(b[i].Length) : string.CompareOrdinal(a[i], b[i]) :
                an != bn ? an ? -1 : 1 : string.CompareOrdinal(a[i], b[i]);
            if (result != 0) return result;
        }
        return a.Length.CompareTo(b.Length);
    }
}

public sealed record UpdateAsset(string Name, Uri Url, long Size, string Sha256);
public sealed record AvailableUpdate(ReleaseVersion Version, string Tag, Uri Page, bool Preview, UpdateAsset Asset);
public sealed record UpdateDeployment(string Root, string Runtime, bool Installed)
{
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7481F09E-89BC-44F4-9A51-66F31CDB4869}_is1";
    public static string RuntimeFor(Architecture architecture) => architecture switch {
        Architecture.X64 => "win-x64", Architecture.X86 => "win-x86", Architecture.Arm64 => "win-arm64", _ => throw new PlatformNotSupportedException()
    };
    public static bool RegisteredAt(string root)
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 }) {
            try {
                using var hive = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                using var key = hive.OpenSubKey(UninstallKey);
                if (key?.GetValue("InstallLocation") is string location && SamePath(location, root)) return true;
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        }
        return false;
    }
    public static bool SamePath(string a, string b) => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
        Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    public static UpdateDeployment Detect()
    {
        string app = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetFileName(app), "app", StringComparison.OrdinalIgnoreCase)) throw new IOException("请使用安装版或完整解压的便携版进行更新。");
        string root = Path.GetDirectoryName(app)!;
        if (!File.Exists(Path.Combine(root, "Mizu.exe")) || !File.Exists(Path.Combine(app, "Mizu.Updater.exe")))
            throw new IOException("更新助手或启动器缺失，请完整解压新版程序。");
        return new(root, RuntimeFor(RuntimeInformation.ProcessArchitecture), RegisteredAt(root));
    }
}

public sealed class UpdatePlan
{
    public int Format { get; set; } = 1;
    public string Session { get; set; } = "";
    public string TargetRoot { get; set; } = "";
    public string Runtime { get; set; } = "";
    public string Mode { get; set; } = "";
    public string TargetVersion { get; set; } = "";
    public string PackageSha256 { get; set; } = "";
    public int ParentPid { get; set; }
    public long ParentStartedUtcTicks { get; set; }
}
public sealed record PreparedUpdate(string Session, string Directory, string Helper);

public sealed class UpdateService : IDisposable
{
    public const string Repository = "https://github.com/cqc0101/Mizu";
    public const long MaximumPackageBytes = 512L * 1024 * 1024;
    public static string JobsDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mizu-Updates");
    private readonly HttpClient http;
    private readonly string jobsDirectory;
    public UpdateService(HttpMessageHandler? handler = null, string? jobsDirectory = null)
    {
        this.jobsDirectory = jobsDirectory ?? JobsDirectory;
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mizu-Updater/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }
    public static bool TrustedDownload(Uri url) => url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0 &&
        (url.Host == "github.com" || url.Host == "release-assets.githubusercontent.com" || url.Host == "objects.githubusercontent.com" || url.Host == "github-releases.githubusercontent.com");
    private async Task<HttpResponseMessage> GetAsync(Uri url, CancellationToken token)
    {
        for (int redirects = 0; redirects <= 5; redirects++) {
            if (!TrustedDownload(url) && !(url.Host == "api.github.com" && url.Scheme == "https" && url.IsDefaultPort && url.UserInfo.Length == 0))
                throw new IOException("更新下载地址不受信任。");
            var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400) {
                var location = response.Headers.Location; response.Dispose();
                if (location is null) throw new IOException("更新服务器返回了无效地址。");
                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                // Download redirects must stay on GitHub's asset infrastructure.
                if (!TrustedDownload(url)) throw new IOException("更新下载跳转地址不受信任。");
                continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; } catch { response.Dispose(); throw; }
        }
        throw new IOException("更新下载跳转次数过多。");
    }
    private async Task<byte[]> ReadLimitedAsync(Uri url, int limit, CancellationToken token)
    {
        using var response = await GetAsync(url, token);
        if (response.Content.Headers.ContentLength > limit) throw new IOException("更新信息过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream(); var block = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(block, token)) > 0) {
            if (buffer.Length + count > limit) throw new IOException("更新信息过大。");
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }
    private static Uri AssetUri(JsonElement asset, string expectedName, string tag)
    {
        var expected = Repository + "/releases/download/" + Uri.EscapeDataString(tag) + "/" + expectedName;
        var actual = asset.GetProperty("browser_download_url").GetString();
        var canonical = new Uri(expected);
        if (!Uri.TryCreate(actual, UriKind.Absolute, out var parsed) || parsed.Scheme != "https" || parsed.Host != "github.com" ||
            !parsed.IsDefaultPort || parsed.UserInfo.Length != 0 || parsed.Query.Length != 0 || parsed.Fragment.Length != 0 ||
            Uri.UnescapeDataString(parsed.AbsolutePath) != Uri.UnescapeDataString(canonical.AbsolutePath))
            throw new IOException("更新附件地址与版本不一致。");
        return canonical;
    }
    public async Task<AvailableUpdate?> CheckAsync(string currentVersion, string runtime, bool installed, bool includePreview, CancellationToken cancellationToken)
    {
        if (runtime is not ("win-x64" or "win-x86" or "win-arm64")) throw new ArgumentException("Unsupported runtime");
        var current = ReleaseVersion.Parse(currentVersion) ?? throw new ArgumentException("Invalid current version");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var bytes = await ReadLimitedAsync(new("https://api.github.com/repos/cqc0101/Mizu/releases?per_page=100"), 4 * 1024 * 1024, deadline.Token);
        using var document = JsonDocument.Parse(bytes);
        AvailableUpdate? best = null;
        foreach (var release in document.RootElement.EnumerateArray()) {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!Regex.IsMatch(tag, @"^v[0-9A-Za-z.+-]{1,99}$")) continue;
            var version = ReleaseVersion.Parse(tag);
            bool preview = release.GetProperty("prerelease").GetBoolean() || version?.Preview.Length > 0;
            if (version is null || version.CompareTo(current) <= 0 || !includePreview && preview || best is not null && version.CompareTo(best.Version) <= 0) continue;
            string name = $"Mizu-{tag[1..]}-{runtime}" + (installed ? "-setup.exe" : ".zip");
            var assets = release.GetProperty("assets").EnumerateArray().ToArray();
            var matching = assets.Where(a => a.GetProperty("name").GetString() == name && a.GetProperty("state").GetString() == "uploaded").ToArray();
            if (matching.Length != 1) continue;
            var asset = matching[0]; long size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > MaximumPackageBytes) continue;
            var url = AssetUri(asset, name, tag);
            string hash = asset.TryGetProperty("digest", out var digest) ? digest.GetString() ?? "" : "";
            hash = hash.StartsWith("sha256:", StringComparison.Ordinal) ? hash[7..] : "";
            if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) {
                hash = "";
                foreach (string manifestName in new[] { $"Mizu-{tag[1..]}-SHA256SUMS.txt", "SHA256SUMS.txt" }) {
                    var manifest = assets.FirstOrDefault(a => a.GetProperty("name").GetString() == manifestName);
                    if (manifest.ValueKind == JsonValueKind.Undefined) continue;
                    var content = System.Text.Encoding.UTF8.GetString(await ReadLimitedAsync(AssetUri(manifest, manifestName, tag), 65536, deadline.Token));
                    var hashes = content.TrimStart('\uFEFF').Split('\n').Select(line => Regex.Match(line.Trim(), @"^([A-Fa-f0-9]{64})\s+\*?(.+)$"))
                        .Where(m => m.Success && m.Groups[2].Value == name).Select(m => m.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    if (hashes.Length == 1) hash = hashes[0];
                    break;
                }
            }
            if (!Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) continue;
            best = new(version, tag, new(Repository + "/releases/tag/" + tag), preview, new(name, url, size, hash.ToLowerInvariant()));
        }
        return best;
    }
    public async Task<PreparedUpdate> PrepareAsync(AvailableUpdate update, UpdateDeployment deployment, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(deployment.Root);
        if (root == Path.GetPathRoot(root) || UpdateDeployment.SamePath(root, SettingsStore.DataDirectory) ||
            root.StartsWith(SettingsStore.DataDirectory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
            root.StartsWith(jobsDirectory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
            jobsDirectory.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("请把程序放在独立文件夹中再更新。");
        Mizu.Updating.PackageArchive.CheckTree(root);
        using (File.Create(Path.Combine(root, ".mizu-write-" + Guid.NewGuid().ToString("N")), 1, FileOptions.DeleteOnClose)) { }
        await Task.Run(() => CleanupCompletedJobs(jobsDirectory, root), cancellationToken);
        string session = Guid.NewGuid().ToString("N"), job = Path.Combine(jobsDirectory, session);
        Directory.CreateDirectory(job);
        string partial = Path.Combine(job, "package.partial"), package = Path.Combine(job, deployment.Installed ? "package.exe" : "package.zip");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromMinutes(15));
        try {
            using (var response = await GetAsync(update.Asset.Url, deadline.Token)) {
                if (response.Content.Headers.ContentLength is { } length && length != update.Asset.Size) throw new IOException("更新包大小与发布信息不一致。");
                await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
                await using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var block = new byte[65536]; long received = 0; int count;
                var clock = Stopwatch.StartNew(); long lastProgress = -150;
                while ((count = await input.ReadAsync(block, deadline.Token)) > 0) {
                    received += count;
                    if (received > update.Asset.Size) throw new IOException("更新包超出声明的大小。");
                    sha.AppendData(block, 0, count); await output.WriteAsync(block.AsMemory(0, count), deadline.Token);
                    if (received == update.Asset.Size || clock.ElapsedMilliseconds - lastProgress >= 150) {
                        progress?.Report((double)received / update.Asset.Size); lastProgress = clock.ElapsedMilliseconds;
                    }
                }
                if (received != update.Asset.Size || !Convert.ToHexString(sha.GetHashAndReset()).Equals(update.Asset.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("更新包校验失败，旧版保持不变。");
            }
            File.Move(partial, package);
            if (!deployment.Installed) Mizu.Updating.PackageArchive.Inspect(package, deployment.Runtime);
            else Mizu.Updating.PackageArchive.CheckExecutable(package, "");
            cancellationToken.ThrowIfCancellationRequested();
            string helper = Path.Combine(job, "Mizu.Updater.exe");
            File.Copy(Path.Combine(root, "app", "Mizu.Updater.exe"), helper, false);
            using var process = Process.GetCurrentProcess();
            var plan = new UpdatePlan { Session = session, TargetRoot = root, Runtime = deployment.Runtime,
                Mode = deployment.Installed ? "installed" : "portable", TargetVersion = update.Version.Core, PackageSha256 = update.Asset.Sha256,
                ParentPid = process.Id, ParentStartedUtcTicks = process.StartTime.ToUniversalTime().Ticks };
            await File.WriteAllTextAsync(Path.Combine(job, "plan.json"), JsonSerializer.Serialize(plan), cancellationToken);
            return new(session, job, helper);
        } catch {
            // Only download files are removed here; failed recovery backups are retained.
            foreach (string file in new[] { partial, package }) { try { File.Delete(file); } catch (IOException) { } }
            throw;
        }
    }
    public static void CleanupCompletedJobs(string jobsRoot, string targetRoot)
    {
        if (!Directory.Exists(jobsRoot)) return;
        var completed = new List<string>();
        foreach (string directory in Directory.EnumerateDirectories(jobsRoot)) {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            try {
                Mizu.Updating.PackageArchive.CheckTree(directory);
                string journal = Path.Combine(directory, "journal.json"), planFile = Path.Combine(directory, "plan.json");
                if (!File.Exists(journal) || !File.Exists(planFile) || new FileInfo(journal).Length > 65536 || new FileInfo(planFile).Length > 65536) continue;
                using var state = JsonDocument.Parse(File.ReadAllText(journal));
                var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planFile));
                if (state.RootElement.GetProperty("Phase").GetString() == "Completed" && plan?.Session == Path.GetFileName(directory) &&
                    UpdateDeployment.SamePath(plan.TargetRoot, targetRoot)) completed.Add(directory);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException) { }
        }
        // Keep the latest successful backup. Failed/interrupted jobs are never
        // deleted automatically, so their recovery material remains available.
        foreach (string old in completed.OrderByDescending(Directory.GetLastWriteTimeUtc).Skip(1)) {
            try { DeleteOwnedJob(old, jobsRoot); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    private static void DeleteOwnedJob(string path, string boundary)
    {
        string full = Path.GetFullPath(path), root = Path.GetFullPath(boundary).TrimEnd(Path.DirectorySeparatorChar);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("拒绝清理越界更新目录。");
        Mizu.Updating.PackageArchive.CheckTree(full);
        foreach (string file in Directory.GetFiles(full)) { Mizu.Updating.PackageArchive.CheckTree(file); File.Delete(file); }
        foreach (string directory in Directory.GetDirectories(full)) DeleteOwnedJob(directory, root);
        Directory.Delete(full);
    }
    public void Dispose() => http.Dispose();
}
