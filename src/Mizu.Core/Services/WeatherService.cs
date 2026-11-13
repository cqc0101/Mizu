using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace Mizu.Core.Services;

public sealed class WeatherService : IDisposable
{
    private readonly HttpClient client;
    public WeatherService(HttpMessageHandler? handler = null)
    { client = handler is null ? new() : new(handler); client.Timeout = TimeSpan.FromSeconds(12); client.DefaultRequestHeaders.UserAgent.ParseAdd("Mizu/0.1"); }
    public async Task<Place[]> SearchAsync(string query, CancellationToken token)
    {
        if (query.Trim().Length < 2) return [];
        using var response = await client.GetAsync("https://geocoding-api.open-meteo.com/v1/search?count=8&language=zh&format=json&name=" + Uri.EscapeDataString(query.Trim()), token);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        if (!json.RootElement.TryGetProperty("results", out var rows)) return [];
        return rows.EnumerateArray().Select(r => new Place(r.GetProperty("name").GetString()!, r.TryGetProperty("country", out var c) ? c.GetString()! : "",
            r.TryGetProperty("admin1", out var region) ? region.GetString() : null, r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble())).ToArray();
    }
    public async Task<WeatherSnapshot> FetchAsync(Place place, CancellationToken token)
    {
        var url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={place.Latitude}&longitude={place.Longitude}&current=temperature_2m,relative_humidity_2m,is_day,weather_code,wind_speed_10m&hourly=temperature_2m,weather_code,is_day&daily=temperature_2m_max,temperature_2m_min,weather_code&forecast_days=8&timezone=auto");
        using var response = await client.GetAsync(url, token); response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        return Parse(doc.RootElement, DateTimeOffset.UtcNow);
    }
    public static WeatherSnapshot Parse(JsonElement root, DateTimeOffset observedAt)
    {
        var c = root.GetProperty("current"); var daily = root.GetProperty("daily"); var hourly = root.GetProperty("hourly");
        var currentTime = DateTime.Parse(c.GetProperty("time").GetString()!, CultureInfo.InvariantCulture);
        var times = hourly.GetProperty("time").EnumerateArray().Select(x => x.GetString()!).ToArray();
        WeatherHour Hour(int i) => new(DateTime.Parse(times[i], CultureInfo.InvariantCulture).ToString("MM/dd HH:mm"), hourly.GetProperty("temperature_2m")[i].GetDouble(), hourly.GetProperty("weather_code")[i].GetInt32(), hourly.GetProperty("is_day")[i].GetInt32() == 1);
        var next = Enumerable.Range(0, times.Length).Where(i => DateTime.Parse(times[i], CultureInfo.InvariantCulture) > currentTime).Take(72).ToArray();
        var hours = next.Select(Hour).ToArray();
        var floor = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, currentTime.Hour, 0, 0);
        var currentIndex = Array.FindIndex(times, t => DateTime.Parse(t, CultureInfo.InvariantCulture) == floor);
        var days = daily.TryGetProperty("time", out var dates) && daily.TryGetProperty("weather_code", out var codes)
            ? Enumerable.Range(0, dates.GetArrayLength()).Where(i => DateTime.Parse(dates[i].GetString()!, CultureInfo.InvariantCulture).Date > currentTime.Date).Take(7)
                .Select(i => new WeatherDay(DateTime.Parse(dates[i].GetString()!, CultureInfo.InvariantCulture).ToString("MM/dd"), daily.GetProperty("temperature_2m_max")[i].GetDouble(), daily.GetProperty("temperature_2m_min")[i].GetDouble(), codes[i].GetInt32())).ToArray()
            : [];
        return new(c.GetProperty("temperature_2m").GetDouble(), c.GetProperty("weather_code").GetInt32(), c.GetProperty("is_day").GetInt32() == 1,
            daily.GetProperty("temperature_2m_max")[0].GetDouble(), daily.GetProperty("temperature_2m_min")[0].GetDouble(), c.GetProperty("wind_speed_10m").GetDouble(),
            c.GetProperty("relative_humidity_2m").GetInt32(), hours, observedAt)
            { CurrentHour = currentIndex >= 0 ? Hour(currentIndex) : null, Days = days };
    }
    public void Dispose() => client.Dispose();
}
