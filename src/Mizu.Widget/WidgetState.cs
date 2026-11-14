using Mizu.Core;
using Mizu.Core.Services;

namespace Mizu.Widget;

public sealed class WidgetState
{
    public PoetrySnapshot? Poetry { get; set; }
    public bool PoetryFailed { get; set; }
    public NetworkSnapshot? Network { get; set; }
    public MediaSnapshot Media { get; set; } = MediaSnapshot.Empty();
    public SystemSnapshot System { get; set; } = new(null, null, null, "");
    public IReadOnlyList<Peripheral> Devices { get; set; } = [];
    public WeatherSnapshot? Weather { get; set; }
    public bool WeatherCached { get; set; }
    public bool WeatherFailed { get; set; }
    public bool Scanning { get; set; }
    public int DevicePage { get; set; }
    public Queue<double> CpuHistory { get; } = new();
    public Queue<double> MemoryHistory { get; } = new();
    public void Record(SystemSnapshot snapshot)
    {
        System = snapshot;
        if (snapshot.CpuPercent is { } cpu) { CpuHistory.Enqueue(cpu); if (CpuHistory.Count > 40) CpuHistory.Dequeue(); }
        if (snapshot.MemoryPercent is { } mem) { MemoryHistory.Enqueue(mem); if (MemoryHistory.Count > 40) MemoryHistory.Dequeue(); }
    }
    public static WidgetState Preview() => new()
    {
        System = new(18, 42, 46, "预览数据"),
        Devices = [new("preview-keyboard", "Alto Keys K98M", PeripheralKind.Keyboard, LinkKind.Receiver, LinkState.Online, 82, false, "Preview", DateTimeOffset.UtcNow),
            new("preview-mouse", "ATK A9 Ultra Max", PeripheralKind.Mouse, LinkKind.Receiver, LinkState.Online, 64, false, "Preview", DateTimeOffset.UtcNow)],
        Weather = new(23, 2, true, 26, 18, 8, 54, Enumerable.Range(0, 72).Select(i => new WeatherHour($"11/{13 + (14 + i) / 24:00} {(14 + i) % 24:00}:00", 22 + Math.Sin(i * .5) * 3, i % 4, i % 24 < 12)).ToArray(), DateTimeOffset.UtcNow)
        { CurrentHour = new("11/13 13:00", 23, 2, true), Days = Enumerable.Range(0, 7).Select(i => new WeatherDay($"11/{14 + i}", 25 - i, 18 - i, i % 4)).ToArray() }
    };
}

public sealed class WidgetEngine(AppSettings settings, WidgetState state, Action changed, PoetryService? poetryService = null) : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly TelemetryService telemetry = new();
    public BatteryService Batteries { get; } = new();
    public WeatherService Weather { get; } = new();
    public NotificationService Notifications { get; } = new();
    public MediaService Media { get; } = new();
    private readonly PoetryService poetry = poetryService ?? new();
    private readonly SemaphoreSlim poetryGate = new(1);
    private bool poetryLoaded;
    private long nextPoetryAttempt;
    private readonly NetworkService network = new();
    private Task[] tasks = [];
    private BluetoothMonitor? bluetooth;
    private readonly SemaphoreSlim connectionSignal = new(0, 1);
    private int started, pendingDeviceRefresh;
    private readonly SemaphoreSlim scan = new(1);
    private readonly SemaphoreSlim weather = new(1);
    private bool pendingWeather;
    private readonly WeatherCache weatherCache = new();
    private Place? weatherPlace;
    private long nextWeatherAttempt;
    public void ClearWeatherCache()
    {
        weatherCache.Clear();
        if (state.WeatherCached) { state.Weather = null; state.WeatherCached = false; changed(); }
        Interlocked.Exchange(ref nextWeatherAttempt, 0);
    }
    public void Start()
    {
        if (Interlocked.Exchange(ref started, 1) != 0) return;
        bluetooth = new BluetoothMonitor(RequestConnectionRefresh); bluetooth.Start();
        tasks = [Task.Run(SampleLoop), Task.Run(DeviceLoop), Task.Run(WeatherLoop), Task.Run(NetworkLoop), Task.Run(MediaLoop), Task.Run(PoetryLoop), Task.Run(ConnectionLoop)];
    }
    private void PublishDevices(IReadOnlyList<Peripheral> ignored)
    {
        if (stop.IsCancellationRequested) return;
        Application.Current.Dispatcher.BeginInvoke(new Action(() => { if (!stop.IsCancellationRequested) { state.Devices = Batteries.Current; changed(); } }));
    }
    public void RequestConnectionRefresh()
    {
        if (stop.IsCancellationRequested || Volatile.Read(ref started) == 0) return;
        if (bluetooth is not null) Batteries.BluetoothLinks = bluetooth.Snapshot();
        PublishDevices([]);
        try { if(connectionSignal.CurrentCount == 0) connectionSignal.Release(); } catch(SemaphoreFullException) { }
        _ = RefreshDevicesAsync();
    }
    public string[] BluetoothSummary => bluetooth?.Snapshot().Select(l => $"{l.Name}: {l.Kind} / {(l.Connected is true ? "Connected" : l.Connected is false ? "Disconnected" : "Unknown")}").ToArray() ?? [];
    private async Task ConnectionLoop()
    {
        try {
            while (!stop.IsCancellationRequested) {
                await connectionSignal.WaitAsync(TimeSpan.FromSeconds(3), stop.Token);
                await Task.Delay(180, stop.Token);
                if (bluetooth is not null) Batteries.BluetoothLinks = bluetooth.Snapshot();
                try {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(6));
                    await Task.Run(() => Batteries.RefreshFastAsync(deadline.Token, PublishDevices), deadline.Token);
                } catch(OperationCanceledException) when (!stop.IsCancellationRequested) { }
                catch(Exception ex) when (ex is not OperationCanceledException) { AppLog.Write("devices.fast", ex.GetType().Name); }
            }
        } catch(OperationCanceledException) { }
    }
    private async Task PoetryLoop()
    {
        try {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do { if (settings.ShowPoetry && DateTime.UtcNow.Ticks >= Interlocked.Read(ref nextPoetryAttempt)) await RefreshPoetryAsync(); }
            while (await timer.WaitForNextTickAsync(stop.Token));
        } catch (OperationCanceledException) { }
    }
    public async Task RefreshPoetryAsync(bool force = false)
    {
        if (!settings.ShowPoetry || !await poetryGate.WaitAsync(0, stop.Token)) return;
        bool preserveRetryDeadline = false;
        try {
            if (!poetryLoaded) {
                poetryLoaded = true; var cached = poetry.Load();
                if (cached is null || (cached.Source == "内置公版诗库" && settings.PoetryAutoRefresh && cached.FetchedAt.ToLocalTime().Date != DateTimeOffset.Now.Date)) {
                    cached = OfflinePoetry.Pick(DateTimeOffset.Now); poetry.Save(cached);
                }
                await Application.Current.Dispatcher.InvokeAsync(() => { state.Poetry = cached; changed(); });
            }
            if (force && state.PoetryFailed && DateTime.UtcNow.Ticks < Interlocked.Read(ref nextPoetryAttempt)) {
                preserveRetryDeadline = true;
                var selected = OfflinePoetry.Pick(DateTimeOffset.Now, state.Poetry?.Content, true); poetry.Save(selected);
                await Application.Current.Dispatcher.InvokeAsync(() => { state.Poetry = selected; changed(); }); return;
            }
            if (!force && state.Poetry is { Dynasty.Length: > 0 } existing &&
                (!settings.PoetryAutoRefresh || (!state.PoetryFailed && existing.Source != "内置公版诗库" && existing.FetchedAt.ToLocalTime().Date == DateTimeOffset.Now.Date))) return;
            var result = await poetry.FetchAsync(stop.Token);
            if (!settings.ShowPoetry) return;
            poetry.Save(result);
            await Application.Current.Dispatcher.InvokeAsync(() => { state.Poetry = result; state.PoetryFailed = false; changed(); });
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) {
            AppLog.Write("poetry", ex.GetType().Name);
            if (!settings.ShowPoetry || stop.IsCancellationRequested) return;
            await Application.Current.Dispatcher.InvokeAsync(() => {
                var now = DateTimeOffset.Now;
                if (force || state.Poetry is null || string.IsNullOrWhiteSpace(state.Poetry.Dynasty) || state.Poetry.FetchedAt.ToLocalTime().Date != now.Date) {
                    state.Poetry = OfflinePoetry.Pick(now, state.Poetry?.Content, force); poetry.Save(state.Poetry);
                }
                state.PoetryFailed = true; changed();
            });
        }
        finally { if (!preserveRetryDeadline) Interlocked.Exchange(ref nextPoetryAttempt, DateTime.UtcNow.AddSeconds(state.PoetryFailed ? 300 : 60).Ticks); poetryGate.Release(); }
    }
    private async Task NetworkLoop()
    {
        try {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            do {
                if (!settings.ShowNetwork) { network.Reset(); continue; }
                try {
                    var result = await network.SampleAsync(settings.NetworkProbeTargets?.ToArray() ?? new[] { settings.NetworkProbeTarget }, stop.Token);
                    await Application.Current.Dispatcher.InvokeAsync(() => { state.Network = result; changed(); });
                } catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
                catch (Exception ex) { AppLog.Write("network", ex.GetType().Name); await Application.Current.Dispatcher.InvokeAsync(() => { state.Network = null; changed(); }); }
            } while (await timer.WaitForNextTickAsync(stop.Token));
        } catch (OperationCanceledException) { }
    }
    private async Task MediaLoop()
    {
        try {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do {
                if (!settings.ShowMedia) continue;
                MediaSnapshot result;
                try { result = await Media.ReadAsync(stop.Token); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { throw; }
                catch (Exception ex) { AppLog.Write("media", ex.GetType().Name); result = MediaSnapshot.Empty("暂时无法读取系统媒体会话"); }
                await Application.Current.Dispatcher.InvokeAsync(() => { state.Media = result; changed(); });
            } while (await timer.WaitForNextTickAsync(stop.Token));
        } catch (OperationCanceledException) { }
    }
    public async Task MediaCommandAsync(string command)
    {
        if (!settings.ShowMedia) return;
        try { if (!await Media.CommandAsync(command, stop.Token)) AppLog.Write("media.command", "Player did not accept command"); }
        catch (Exception ex) { AppLog.Write("media.command", ex.GetType().Name); }
    }
    private async Task SampleLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            do
            {
                var snapshot = telemetry.Sample(settings.TemperatureEnabled);
                await Application.Current.Dispatcher.InvokeAsync(() => { state.Record(snapshot); changed(); });
                _ = NotifySafely(snapshot);
            } while (await timer.WaitForNextTickAsync(stop.Token));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write("engine.system", ex.GetType().Name); }
    }
    private async Task NotifySafely(SystemSnapshot snapshot)
    { try { await Notifications.EvaluateAsync(settings, snapshot, state.Devices, stop.Token); } catch (OperationCanceledException) { } catch (Exception ex) { AppLog.Write("engine.push", ex.GetType().Name); } }
    private async Task DeviceLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            do { await RefreshDevicesAsync(); } while (await timer.WaitForNextTickAsync(stop.Token));
        }
        catch (OperationCanceledException) { }
    }
    public async Task RefreshDevicesAsync()
    {
        if (stop.IsCancellationRequested) return;
        if (!await scan.WaitAsync(0, stop.Token)) { Interlocked.Exchange(ref pendingDeviceRefresh, 1); return; }
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() => { state.Scanning = true; changed(); });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(40));
            var devices = await Task.Run(() => Batteries.ScanAsync(deadline.Token, PublishDevices), deadline.Token);
            await Application.Current.Dispatcher.InvokeAsync(() => { state.Devices = Batteries.Current; changed(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write("engine.devices", ex.GetType().Name); }
        finally
        {
            if (!stop.IsCancellationRequested) await Application.Current.Dispatcher.InvokeAsync(() => { state.Scanning = false; changed(); });
            scan.Release();
            if (Interlocked.Exchange(ref pendingDeviceRefresh, 0) != 0 && !stop.IsCancellationRequested) _ = RefreshDevicesAsync();
        }
    }
    private async Task WeatherLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do { if (DateTime.UtcNow.Ticks >= Interlocked.Read(ref nextWeatherAttempt)) await RefreshWeatherAsync(); }
            while (await timer.WaitForNextTickAsync(stop.Token));
        }
        catch (OperationCanceledException) { }
    }
    public async Task RefreshWeatherAsync()
    {
        if (!await weather.WaitAsync(0, stop.Token)) { pendingWeather = true; return; }
        var location = settings.Location;
        bool success = false;
        try
        {
            if (location != weatherPlace)
            {
                weatherPlace = location;
                var cached = location is not null && settings.WeatherCacheEnabled ? weatherCache.Load(location, settings.WeatherCacheHours, DateTimeOffset.UtcNow) : null;
                await Application.Current.Dispatcher.InvokeAsync(() => { state.Weather = cached; state.WeatherCached = cached is not null; state.WeatherFailed = false; changed(); });
            }
            if (location is null)
            { await Application.Current.Dispatcher.InvokeAsync(() => { state.Weather = null; state.WeatherCached = false; state.WeatherFailed = false; changed(); }); return; }
            var result = await Weather.FetchAsync(location, stop.Token, settings.WeatherBackupEnabled);
            if (location != settings.Location) return;
            success = true;
            if (settings.WeatherCacheEnabled) weatherCache.Save(location, result);
            await Application.Current.Dispatcher.InvokeAsync(() => { state.Weather = result; state.WeatherCached = false; state.WeatherFailed = false; changed(); });
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex)
        {
            AppLog.Write("weather", ex.GetType().Name + (ex is System.Net.Http.HttpRequestException http ? $" HTTP {(int?)http.StatusCode}" : ""));
            if (location == settings.Location && !stop.IsCancellationRequested)
                await Application.Current.Dispatcher.InvokeAsync(() => { state.WeatherFailed = true; changed(); });
        }
        finally
        {
            if (location == settings.Location) Interlocked.Exchange(ref nextWeatherAttempt, DateTime.UtcNow.AddSeconds(success ? 1200 : 30).Ticks);
            weather.Release(); if (pendingWeather && !stop.IsCancellationRequested) { pendingWeather = false; _ = RefreshWeatherAsync(); }
        }
    }
    public async ValueTask DisposeAsync()
    {
        bluetooth?.Dispose();
        await stop.CancelAsync();
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        // Dispose hardware only once the sampling worker has stopped.
        if (tasks.Length == 0 || tasks[0].IsCompleted) telemetry.Dispose();
        Weather.Dispose(); poetry.Dispose(); Notifications.Dispose(); stop.Dispose();
    }
}
