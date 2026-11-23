using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using Mizu.Core.Services;

namespace Mizu.Widget;

[Flags]
internal enum LocalCleanup
{
    None = 0, Weather = 1, Poetry = 2, Battery = 4, Vocabulary = 8, Todos = 16, Display = 32
}

internal sealed class CloudBackupCoordinator : IDisposable
{
    private readonly MainWindow widget;
    private readonly FirebaseSyncClient firebase = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly DispatcherTimer timer = new();
    private bool started, disposed;
    public FirebaseConnection? Connection { get; private set; }
    public bool Busy { get; private set; }
    public DateTimeOffset? NextBackupAt { get; private set; }
    public string Message { get; private set; } = "";
    public event Action? Changed;

    public CloudBackupCoordinator(MainWindow widget)
    {
        this.widget = widget;
        if (widget.SyncAvailable)
        {
            Connection = FirebaseConnection.Restore(widget.Settings.ProtectedFirebaseSync);
            if (widget.Settings.ProtectedFirebaseSync is not null && Connection is null)
                Message = "本机无法读取已保存的登录，请重新登录。";
        }
        timer.Tick += async (_, _) => await UploadAsync(automatic: true, confirm: null, CancellationToken.None);
    }

    public void Start()
    {
        if (started || !widget.SyncAvailable || disposed) return;
        started = true;
        Schedule();
    }

    public void Schedule()
    {
        timer.Stop();
        NextBackupAt = null;
        if (started && !disposed && !Busy && Connection is not null && widget.Settings.AutoCloudBackup)
        {
            timer.Interval = TimeSpan.FromMinutes(Math.Clamp(widget.Settings.CloudBackupIntervalMinutes, 1, 720));
            NextBackupAt = DateTimeOffset.Now + timer.Interval;
            timer.Start();
        }
        if (!disposed) Changed?.Invoke();
    }

    public Task<bool> SignInAsync(FirebaseConfig config, string email, string password, CancellationToken token) =>
        RunAsync("正在登录 Firebase…", async ct =>
        {
            var login = await firebase.SignInAsync(config, email, password, ct);
            ct.ThrowIfCancellationRequested();
            Save(login);
            Message = "登录成功，可以上传或恢复备份。";
            return true;
        }, token);

    public Task<bool> SignOutAsync(CancellationToken token) =>
        RunAsync("正在退出…", _ =>
        {
            widget.SaveFirebaseSync(null, pauseAutomaticBackup: true);
            Connection = null;
            Message = "已退出，云端备份已保留。";
            return Task.FromResult(true);
        }, token);

    public Task<bool> UploadAsync(bool automatic, Func<FirestoreSnapshot, bool>? confirm, CancellationToken token) =>
        RunAsync(automatic ? "正在检查自动备份…" : "正在检查云端备份…", async ct =>
        {
            if (Connection is not { } login || (automatic && !widget.Settings.AutoCloudBackup)) return false;
            var bytes = SyncSnapshot.Capture(widget.Settings).Encode();
            string localHash = SyncSnapshot.Hash(bytes);
            if (automatic && localHash == login.LastSyncHash)
            {
                Message = "本机内容无变化，等待下次自动备份。";
                return true;
            }

            var access = await RefreshAsync(login, ct);
            var cloud = await firebase.DownloadAsync(access, ct);
            ct.ThrowIfCancellationRequested();
            // Include edits made while the cloud read was in flight.
            bytes = SyncSnapshot.Capture(widget.Settings).Encode();
            localHash = SyncSnapshot.Hash(bytes);
            string? cloudHash = cloud is null ? null : SyncSnapshot.Hash(cloud.Snapshot.Encode());
            var decision = SyncSnapshot.DecideUpload(localHash, cloudHash, login.LastSyncHash);
            if (decision == CloudUploadDecision.CloudChanged)
            {
                if (automatic)
                {
                    Message = "云端有其他版本，自动备份已跳过。可手动上传或恢复。";
                    return false;
                }
                if (confirm?.Invoke(cloud!) != true) { Message = "已取消上传。"; return false; }
                ct.ThrowIfCancellationRequested();
                if (localHash != SyncSnapshot.Hash(SyncSnapshot.Capture(widget.Settings).Encode()))
                {
                    Message = "本机内容在确认期间有变化，请重新上传。";
                    return false;
                }
            }
            if (decision != CloudUploadDecision.AlreadyCurrent)
            {
                await firebase.UploadAsync(access, bytes, cloud?.Revision, ct);
                ct.ThrowIfCancellationRequested();
            }
            Save(access.Connection with { LastSyncHash = localHash, LastSyncAt = DateTimeOffset.UtcNow });
            Message = decision == CloudUploadDecision.AlreadyCurrent ? "云端已有相同的备份。"
                : automatic ? "自动备份已上传至云端。" : "备份已上传至云端。";
            return true;
        }, token);

    public Task<bool> RestoreCloudAsync(Func<FirestoreSnapshot, bool> confirm, CancellationToken token) =>
        RunAsync("正在读取云端备份…", async ct =>
        {
            if (Connection is not { } login) return false;
            var access = await RefreshAsync(login, ct);
            var cloud = await firebase.DownloadAsync(access, ct);
            ct.ThrowIfCancellationRequested();
            if (cloud is null) { Message = "云端还没有备份，请先上传。"; return false; }
            string localHash = SyncSnapshot.Hash(SyncSnapshot.Capture(widget.Settings).Encode());
            string cloudHash = SyncSnapshot.Hash(cloud.Snapshot.Encode());
            var next = access.Connection with { LastSyncHash = cloudHash, LastSyncAt = DateTimeOffset.UtcNow };
            if (localHash == cloudHash)
            {
                Save(next);
                Message = "本机内容已与云端备份一致。";
                return false;
            }
            if (!confirm(cloud)) { Message = "已取消恢复。"; return false; }
            ct.ThrowIfCancellationRequested();
            if (localHash != SyncSnapshot.Hash(SyncSnapshot.Capture(widget.Settings).Encode()))
            {
                Message = "本机内容在确认期间有变化，请重新恢复。";
                return false;
            }
            widget.SaveFirebaseSync(next, cloud.Snapshot);
            Connection = next;
            Message = "已恢复云端备份。恢复前的本机数据也已备份。";
            return true;
        }, token);

    public Task<bool> RestoreLocalAsync(string path, Func<bool> confirm, CancellationToken token) =>
        RunAsync("正在恢复本机备份…", ct =>
        {
            if (!confirm()) { Message = "已取消恢复。"; return Task.FromResult(false); }
            ct.ThrowIfCancellationRequested();
            if (new FileInfo(path).Length > SyncSnapshot.MaximumBytes) throw new InvalidDataException("本机备份超过容量限制。");
            var data = SyncSnapshot.Decode(File.ReadAllBytes(path));
            widget.SaveFirebaseSync(Connection, data, path, pauseAutomaticBackup: true);
            Message = "已恢复本机备份，自动备份已暂停。";
            return Task.FromResult(true);
        }, token);

    public Task<bool> CleanupAsync(LocalCleanup selection, Func<bool> confirm, CancellationToken token) =>
        RunAsync("正在清理所选内容…", async ct =>
        {
            if (selection == LocalCleanup.None || !confirm()) { Message = "已取消清理。"; return false; }
            ct.ThrowIfCancellationRequested();
            var data = selection & (LocalCleanup.Vocabulary | LocalCleanup.Todos | LocalCleanup.Display);
            if (data != LocalCleanup.None)
            {
                var reset = SyncSnapshot.Capture(widget.Settings).ClearLocalData(
                    data.HasFlag(LocalCleanup.Vocabulary), data.HasFlag(LocalCleanup.Todos), data.HasFlag(LocalCleanup.Display));
                widget.SaveFirebaseSync(Connection, reset, MainWindow.CleanupBackupPath, pauseAutomaticBackup: true);
            }
            var failed = new List<string>();
            async Task Clear(LocalCleanup option, string name, Func<CancellationToken, Task> clear)
            {
                if (!selection.HasFlag(option)) return;
                try { await clear(ct); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { failed.Add(name); }
            }
            await Clear(LocalCleanup.Weather, "天气", widget.Engine.ClearWeatherFilesAsync);
            await Clear(LocalCleanup.Poetry, "诗词", widget.Engine.ClearPoetryFilesAsync);
            await Clear(LocalCleanup.Battery, "外设读数", widget.Engine.Batteries.ClearCacheAsync);
            Message = data != LocalCleanup.None ? "所选本机数据已清空，自动备份已暂停。"
                : failed.Count == 0 ? "已清除所选缓存。" : "";
            if (failed.Count > 0) Message += "未能清理以下缓存：" + string.Join("、", failed) + "。";
            return data != LocalCleanup.None;
        }, token);

    private void Save(FirebaseConnection connection)
    {
        widget.SaveFirebaseSync(connection);
        Connection = connection;
    }

    private async Task<FirebaseAccess> RefreshAsync(FirebaseConnection login, CancellationToken token)
    {
        var access = await firebase.RefreshAsync(login, token);
        token.ThrowIfCancellationRequested();
        if (access.Connection.RefreshToken != login.RefreshToken) Save(access.Connection);
        return access;
    }

    private async Task<bool> RunAsync(string progress, Func<CancellationToken, Task<bool>> operation, CancellationToken caller)
    {
        if (disposed || Busy || !widget.SyncAvailable || caller.IsCancellationRequested) return false;
        Busy = true;
        timer.Stop();
        NextBackupAt = null;
        Message = progress;
        Changed?.Invoke();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, caller);
        timeout.CancelAfter(TimeSpan.FromSeconds(55));
        try { return await operation(timeout.Token); }
        catch (OperationCanceledException) { Message = caller.IsCancellationRequested ? "操作已取消。" : "请求超时，请重试。"; }
        catch (FirebaseRequestException ex) { Message = ex.Message; }
        catch (InvalidDataException ex) { Message = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException)
        { Message = "无法读取备份或 Firebase 响应，请检查网络与项目设置。"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        { Message = "无法保存或读取本机数据，请检查存储权限和空间。"; }
        finally { Busy = false; Schedule(); }
        return false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop();
        lifetime.Cancel(); lifetime.Dispose();
    }
}
