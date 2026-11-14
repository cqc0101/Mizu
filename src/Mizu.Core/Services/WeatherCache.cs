using System.Text.Json;
namespace Mizu.Core.Services;

public sealed class WeatherCache(string? path = null)
{
    private readonly string file = path ?? Path.Combine(SettingsStore.DataDirectory, "weather-cache.json");
    private sealed record Entry(int Version, double Latitude, double Longitude, WeatherSnapshot Snapshot);
    public WeatherSnapshot? Load(Place place, int maxHours, DateTimeOffset now)
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 2_000_000) return null;
            var e = JsonSerializer.Deserialize<Entry>(File.ReadAllText(file));
            if (e is null || e.Version != 1 || e.Latitude != place.Latitude || e.Longitude != place.Longitude || e.Snapshot is not { } s) return null;
            if (s.UpdatedAt > now || now - s.UpdatedAt > TimeSpan.FromHours(Math.Clamp(maxHours, 1, 72))) return null;
            if (!double.IsFinite(s.Celsius) || !double.IsFinite(s.High) || !double.IsFinite(s.Low) || s.Hours is null || s.Days is null) return null;
            return s;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { AppLog.Write("weather.cache.read", ex.GetType().Name); return null; }
    }
    public void Save(Place place, WeatherSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new Entry(1, place.Latitude, place.Longitude, snapshot)));
            File.Move(file + ".tmp", file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("weather.cache.save", ex.GetType().Name); }
    }
    public void Clear()
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("weather.cache.clear", ex.GetType().Name); }
    }
}
