using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mizu.Core.Services;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Scale { get; set; } = 1;
    public double TextScale { get; set; } = 1;
    public bool SmoothResize { get; set; } = true;
    public int TextWeight { get; set; } = 400;
    public double GlassOpacity { get; set; } = .62;
    public string Theme { get; set; } = "light";
    public double CornerRadius { get; set; } = 30;
    public bool CompactMode { get; set; }
    public bool ShowWeather { get; set; } = true;
    public bool ShowDevices { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowMemory { get; set; } = true;
    public bool ShowTemperature { get; set; } = true;
    public bool ShowTodo { get; set; }
    public bool ShowGraphs { get; set; } = true;
    public bool ShowConnectionIcons { get; set; } = true;
    public bool WeatherCurrent { get; set; } = true;
    public bool WeatherTodayRange { get; set; } = true;
    public bool WeatherHourly { get; set; } = true;
    public bool WeatherDaily { get; set; }
    public bool HourlyIncludeCurrent { get; set; }
    public int ForecastHours { get; set; } = 5;
    public int HourStep { get; set; } = 1;
    public int ForecastDays { get; set; } = 3;
    public bool HideCompletedTodos { get; set; } = true;
    public int TodoVisibleRows { get; set; } = 4;
    public List<TodoItem> Todos { get; set; } = [];
    public bool AlwaysOnTop { get; set; }
    public bool ClickThrough { get; set; }
    public bool AutoStart { get; set; }
    public bool TemperatureEnabled { get; set; }
    public Place? Location { get; set; }
    public bool PushEnabled { get; set; }
    public string? ProtectedPushToken { get; set; }
    public int BatteryThreshold { get; set; } = 20;
    public double CpuThreshold { get; set; } = 90;
    public double MemoryThreshold { get; set; } = 90;
    public double TemperatureThreshold { get; set; } = 85;
    public int CooldownMinutes { get; set; } = 120;
    public int SustainedSeconds { get; set; } = 60;
    public Dictionary<string, PeripheralKind> TypeOverrides { get; set; } = [];
    public Dictionary<string, string> NameOverrides { get; set; } = [];
    public HashSet<string> HiddenDevices { get; set; } = [];

    public void Validate()
    {
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, .75, 1.5) : 1;
        TextScale = double.IsFinite(TextScale) ? Math.Clamp(TextScale, .85, 1.25) : 1;
        TextWeight = Math.Clamp(TextWeight, 300, 700);
        GlassOpacity = double.IsFinite(GlassOpacity) ? Math.Clamp(GlassOpacity, .15, .95) : .62;
        CornerRadius = double.IsFinite(CornerRadius) ? Math.Clamp(CornerRadius, 0, 64) : 30;
        ForecastHours = Math.Clamp(ForecastHours, 1, 12); HourStep = Math.Clamp(HourStep, 1, 6);
        ForecastDays = Math.Clamp(ForecastDays, 1, 7); TodoVisibleRows = Math.Clamp(TodoVisibleRows, 1, 8);
        Todos = (Todos ?? []).Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Text)).Take(200).ToList();
        foreach (var todo in Todos) { todo.Text = todo.Text.Trim()[..Math.Min(todo.Text.Trim().Length, 160)]; if (string.IsNullOrWhiteSpace(todo.Id)) todo.Id = Guid.NewGuid().ToString("N"); }
        BatteryThreshold = Math.Clamp(BatteryThreshold, 1, 99);
        CpuThreshold = double.IsFinite(CpuThreshold) ? Math.Clamp(CpuThreshold, 1, 100) : 90;
        MemoryThreshold = double.IsFinite(MemoryThreshold) ? Math.Clamp(MemoryThreshold, 1, 100) : 90;
        TemperatureThreshold = double.IsFinite(TemperatureThreshold) ? Math.Clamp(TemperatureThreshold, 40, 115) : 85;
        CooldownMinutes = Math.Clamp(CooldownMinutes, 5, 1440);
        SustainedSeconds = Math.Clamp(SustainedSeconds, 10, 600);
        if (Left is { } l && !double.IsFinite(l)) Left = null;
        if (Top is { } t && !double.IsFinite(t)) Top = null;
        if (Location is { } p && (!double.IsFinite(p.Latitude) || !double.IsFinite(p.Longitude) || Math.Abs(p.Latitude) > 90 || Math.Abs(p.Longitude) > 180)) Location = null;
        if (Theme is not ("light" or "dark" or "navy" or "pink" or "lavender")) Theme = "light";
        TypeOverrides ??= []; NameOverrides ??= []; HiddenDevices ??= [];
    }
}

public sealed class TodoItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool Completed { get; set; }
}

public sealed class SettingsStore
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mizu");
    public string FilePath { get; }
    public SettingsStore(string? path = null) => FilePath = path ?? Path.Combine(DataDirectory, "settings.json");
    public AppSettings Load()
    {
        try { var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); s.Validate(); return s; }
        catch (FileNotFoundException) { return new(); }
        catch (DirectoryNotFoundException) { return new(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { AppLog.Write("settings.load", ex.GetType().Name); return new(); }
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.Move(temp, FilePath, true);
    }
    public static string? Protect(string token) => string.IsNullOrWhiteSpace(token) ? null :
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()), null, DataProtectionScope.CurrentUser));
    public static string? Unprotect(string? token)
    {
        try { return string.IsNullOrEmpty(token) ? null : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(token), null, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }
}

public static class AppLog
{
    private static readonly object Gate = new();
    // Never log tokens, HTTP bodies or raw device paths.
    public static void Write(string area, string detail)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.DataDirectory);
                var path = Path.Combine(SettingsStore.DataDirectory, "mizu.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512_000) File.Move(path, path + ".old", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{area}] {detail}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
