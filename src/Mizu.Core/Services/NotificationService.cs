using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mizu.Core.Services;

public sealed record Alert(string Key, string Title, string Content);
public interface INotificationChannel { Task SendAsync(Alert alert, CancellationToken token); }

public sealed class PushPlusChannel(string token, HttpClient client) : INotificationChannel
{
    public async Task SendAsync(Alert alert, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync("https://www.pushplus.plus/send", new { token, title = alert.Title, content = alert.Content, template = "txt", channel = "wechat" }, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!body.RootElement.TryGetProperty("code", out var code) || code.GetInt32() != 200) throw new HttpRequestException("PushPlus 未接受推送请求");
        // HTTP 200 / application code 200 means accepted, not confirmed delivery to WeChat.
    }
}

public sealed class AlertRules
{
    private readonly Dictionary<string, DateTimeOffset> since = [];
    private readonly Dictionary<string, DateTimeOffset> sent = [];
    private readonly Dictionary<string, DateTimeOffset> retryAfter = [];
    private DateTimeOffset? previousEvaluation;
    public IReadOnlyList<Alert> Evaluate(AppSettings s, SystemSnapshot system, IReadOnlyList<Peripheral> devices, DateTimeOffset now)
    {
        var alerts = new List<Alert>();
        // Suspension / a stalled sampler cannot establish sustained load.
        if (previousEvaluation is { } previous && (now < previous || now - previous > TimeSpan.FromSeconds(10))) since.Clear();
        previousEvaluation = now;
        bool Ready(string key) => (!sent.TryGetValue(key, out var last) || now - last >= TimeSpan.FromMinutes(s.CooldownMinutes))
            && (!retryAfter.TryGetValue(key, out var next) || now >= next);
        void Sustained(string key, double? value, double threshold, string title, string suffix)
        {
            if (value is null || value < threshold) { since.Remove(key); return; }
            if (!since.TryGetValue(key, out var began)) { since[key] = now; return; }
            if (now - began >= TimeSpan.FromSeconds(s.SustainedSeconds) && Ready(key)) alerts.Add(new(key, title, $"{value:0}{suffix} · 已持续 {(now - began).TotalSeconds:0} 秒"));
        }
        Sustained("cpu", system.CpuPercent, s.CpuThreshold, "CPU 负载较高", "%");
        Sustained("memory", system.MemoryPercent, s.MemoryThreshold, "内存占用较高", "%");
        Sustained("temperature", system.CpuCelsius, s.TemperatureThreshold, "CPU 温度较高", "°C");
        foreach (var d in devices)
        {
            if (d.State == LinkState.Online && d.IsFresh(now) && d.Percent is { } p && p <= s.BatteryThreshold && d.Charging != true && Ready(d.Id))
                alerts.Add(new(d.Id, "外设电量偏低", $"{d.Name} · {p}%"));
            if (d.Percent > s.BatteryThreshold + 5) sent.Remove(d.Id); // Recharge resets the low-battery cycle.
        }
        return alerts;
    }
    public void Succeeded(string key, DateTimeOffset now) { sent[key] = now; retryAfter.Remove(key); }
    public void Failed(string key, DateTimeOffset now) => retryAfter[key] = now.AddMinutes(5);
}

public sealed class NotificationService : IDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly AlertRules rules = new();
    private readonly SemaphoreSlim gate = new(1);
    public string? LastError { get; private set; }
    public async Task EvaluateAsync(AppSettings s, SystemSnapshot snapshot, IReadOnlyList<Peripheral> devices, CancellationToken token)
    {
        if (!s.PushEnabled || SettingsStore.Unprotect(s.ProtectedPushToken) is not { Length: > 0 } secret || !await gate.WaitAsync(0, token)) return;
        try
        {
            var channel = new PushPlusChannel(secret, http);
            foreach (var alert in rules.Evaluate(s, snapshot, devices, DateTimeOffset.UtcNow))
                try { await channel.SendAsync(alert, token); rules.Succeeded(alert.Key, DateTimeOffset.UtcNow); LastError = null; }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                { rules.Failed(alert.Key, DateTimeOffset.UtcNow); LastError = "推送未被服务器接受，稍后重试"; AppLog.Write("push", ex.GetType().Name); }
        }
        finally { gate.Release(); }
    }
    public Task TestAsync(string secret, CancellationToken token) => new PushPlusChannel(secret, http).SendAsync(new("test", "Mizu", "连接测试 · 桌面状态，安静抵达。"), token);
    public void Dispose() => http.Dispose();
}
