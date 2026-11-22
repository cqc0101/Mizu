using System.Diagnostics;
using Mizu.Core.Services;

namespace Mizu.Widget;

public sealed class UpdateCoordinator : IDisposable
{
    private readonly MainWindow widget;
    private readonly bool enabled;
    private readonly UpdateService service;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private readonly System.Windows.Threading.DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(1) };
    private bool preview, started;
    private DateTimeOffset retryAfter;
    private string? notifiedTag;
    public event Action? Changed;
    public AvailableUpdate? Available { get; private set; }
    public string Message { get; private set; } = "可手动检查更新，也可在后台自动检查。";
    public bool Checking { get; private set; }
    public bool Downloading { get; private set; }
    public double Progress { get; private set; }
    public string CurrentVersion => typeof(App).Assembly.GetName().Version!.ToString(3);
    public UpdateCoordinator(MainWindow widget, bool enabled, UpdateService? service = null)
    {
        this.widget = widget; this.enabled = enabled; this.service = service ?? new();
        preview = widget.Settings.ReceivePreviewUpdates;
        timer.Tick += async (_, _) => await AutoCheckAsync();
    }
    public async void Start()
    {
        if (!enabled || started) return;
        started = true; Configure();
        try { await Task.Delay(TimeSpan.FromSeconds(12), lifetime.Token); await AutoCheckAsync(); }
        catch (OperationCanceledException) { }
    }
    public void Configure()
    {
        if (preview != widget.Settings.ReceivePreviewUpdates) {
            preview = widget.Settings.ReceivePreviewUpdates;
            operation?.Cancel(); Available = null;
            Message = "更新通道已更改，可重新检查。"; Changed?.Invoke();
        }
        if (started && widget.Settings.AutoCheckUpdates) timer.Start(); else timer.Stop();
    }
    private async Task AutoCheckAsync()
    {
        if (!enabled || !widget.Settings.AutoCheckUpdates || DateTimeOffset.UtcNow < retryAfter) return;
        var last = widget.Settings.LastUpdateCheckUtc;
        if (last is { } at && at <= DateTimeOffset.UtcNow && DateTimeOffset.UtcNow - at < TimeSpan.FromHours(24)) return;
        await CheckAsync(false);
    }
    public async Task CheckAsync(bool manual = true)
    {
        if (Checking || Downloading) return;
        if (!enabled) { Message = "调试与界面预览不联网检查更新。"; Changed?.Invoke(); return; }
        Checking = true; Available = null; Message = "正在检查更新…"; Changed?.Invoke();
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        bool selectedPreview = preview;
        try {
            var deployment = UpdateDeployment.Detect();
            var available = await service.CheckAsync(CurrentVersion, deployment.Runtime, deployment.Installed, selectedPreview, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            Available = available; widget.Settings.LastUpdateCheckUtc = DateTimeOffset.UtcNow; widget.SaveSettings();
            Message = available is null ? "未发现更新。" : $"发现新版本 {available.Tag}" + (available.Preview ? " · 预览版" : "");
            if (!manual && available is not null && notifiedTag != available.Tag) { notifiedTag = available.Tag; widget.NotifyUpdateAvailable(available.Tag); }
        } catch (OperationCanceledException) { if (!lifetime.IsCancellationRequested && selectedPreview == preview) Message = "检查已取消或超时，可稍后重试。"; }
        catch (Exception ex) { Message = ex is IOException ? ex.Message : "检查失败，请检查网络后重试。"; retryAfter = DateTimeOffset.UtcNow.AddHours(1); AppLog.Write("update.check", ex.GetType().Name); }
        finally { Checking = false; operation.Dispose(); operation = null; Changed?.Invoke(); }
    }
    public async Task DownloadAndApplyAsync()
    {
        if (!enabled || Available is null || Checking || Downloading) return;
        var available = Available;
        Downloading = true; Progress = 0; Message = "正在下载更新…"; Changed?.Invoke();
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        Process? helper = null; bool handedOff = false;
        try {
            var deployment = UpdateDeployment.Detect();
            var downloadOperation = operation;
            var progress = new Progress<double>(value => {
                if (!Downloading || operation != downloadOperation || downloadOperation.IsCancellationRequested) return;
                Progress = value; Message = $"正在下载更新 · {value:P0}"; Changed?.Invoke();
            });
            var prepared = await service.PrepareAsync(available, deployment, progress, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            widget.PersistBeforeUpdate();
            var start = new ProcessStartInfo(prepared.Helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = prepared.Directory };
            start.ArgumentList.Add("--apply"); start.ArgumentList.Add(prepared.Session);
            helper = Process.Start(start);
            if (helper is null) throw new IOException("无法启动更新助手，旧版保持不变。");
            Message = "下载已校验，正在准备重启…"; Changed?.Invoke();
            var wait = Stopwatch.StartNew(); bool ready = false;
            string readyPath = Path.Combine(prepared.Directory, "ready.json");
            while (wait.Elapsed < TimeSpan.FromSeconds(15)) {
                operation.Token.ThrowIfCancellationRequested();
                if (helper.HasExited) throw new IOException("更新助手未能启动，旧版保持不变。");
                if (File.Exists(readyPath)) {
                    using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(readyPath, operation.Token));
                    ready = document.RootElement.GetProperty("Session").GetString() == prepared.Session &&
                        document.RootElement.GetProperty("HelperPid").GetInt32() == helper.Id;
                    if (ready) break;
                }
                await Task.Delay(100, operation.Token);
            }
            if (!ready) throw new IOException("更新助手准备超时，旧版保持不变。");
            handedOff = true;
            Message = "正在重启并完成更新…"; Changed?.Invoke();
            await widget.QuitAsync();
        } catch (OperationCanceledException) { Message = "下载已取消，旧版保持不变。"; }
        catch (Exception ex) { Message = ex is IOException ? ex.Message : "更新下载未完成，旧版保持不变，可重试。"; AppLog.Write("update.download", ex.GetType().Name); }
        finally {
            if (!handedOff && helper is not null) { try { if (!helper.HasExited) helper.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
            helper?.Dispose(); Downloading = false; operation?.Dispose(); operation = null; Changed?.Invoke();
        }
    }
    public void CancelDownload() => operation?.Cancel();
    public void SetStartupMessage(string message) { Message = message; Changed?.Invoke(); }
    public void Dispose() { timer.Stop(); lifetime.Cancel(); service.Dispose(); }
}
