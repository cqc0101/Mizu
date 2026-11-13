using Mizu.Core;
using Mizu.Core.Services;

namespace Mizu.Widget;

public sealed class WidgetState
{
    public SystemSnapshot System { get; set; } = new(null, null, null, "");
    public IReadOnlyList<Peripheral> Devices { get; set; } = [];
    public WeatherSnapshot? Weather { get; set; }
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

public sealed class WidgetEngine(AppSettings settings, WidgetState state, Action changed) : IAsyncDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly TelemetryService telemetry = new();
    public BatteryService Batteries { get; } = new();
    public WeatherService Weather { get; } = new();
    public NotificationService Notifications { get; } = new();
    private Task[] tasks = [];
    private readonly SemaphoreSlim scan = new(1);
    private readonly SemaphoreSlim weather = new(1);
    private bool pendingWeather;
    public void Start() => tasks = [Task.Run(SampleLoop), Task.Run(DeviceLoop), Task.Run(WeatherLoop)];
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
        if (!await scan.WaitAsync(0, stop.Token)) return;
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() => { state.Scanning = true; changed(); });
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(40));
            var devices = await Task.Run(() => Batteries.ScanAsync(deadline.Token), deadline.Token);
            await Application.Current.Dispatcher.InvokeAsync(() => { state.Devices = devices; changed(); });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write("engine.devices", ex.GetType().Name); }
        finally
        {
            if (!stop.IsCancellationRequested) await Application.Current.Dispatcher.InvokeAsync(() => { state.Scanning = false; changed(); });
            scan.Release();
        }
    }
    private async Task WeatherLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(20));
            do { await RefreshWeatherAsync(); } while (await timer.WaitForNextTickAsync(stop.Token));
        }
        catch (OperationCanceledException) { }
    }
    public async Task RefreshWeatherAsync()
    {
        if (!await weather.WaitAsync(0, stop.Token)) { pendingWeather = true; return; }
        var location = settings.Location;
        try
        {
            if (location is null)
            { await Application.Current.Dispatcher.InvokeAsync(() => { state.Weather = null; state.WeatherFailed = false; changed(); }); return; }
            var result = await Weather.FetchAsync(location, stop.Token);
            if (location != settings.Location) return;
            await Application.Current.Dispatcher.InvokeAsync(() => { state.Weather = result; state.WeatherFailed = false; changed(); });
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException or FormatException or InvalidOperationException or OperationCanceledException or KeyNotFoundException)
        { AppLog.Write("weather", ex.GetType().Name); await Application.Current.Dispatcher.InvokeAsync(() => { state.WeatherFailed = true; changed(); }); }
        finally { weather.Release(); if (pendingWeather && !stop.IsCancellationRequested) { pendingWeather = false; _ = RefreshWeatherAsync(); } }
    }
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { }
        // Dispose hardware only once the sampling worker has stopped.
        if (tasks.Length == 0 || tasks[0].IsCompleted) telemetry.Dispose();
        Weather.Dispose(); Notifications.Dispose(); stop.Dispose();
    }
}
