using System.Globalization;
using System.Text.Json;
namespace Mizu.Core.Services;

public static class WttrWeather
{
    public static int ToWmo(int code) => code switch {
        113 => 0, 116 => 2, 119 or 122 => 3, 143 or 149 or 248 => 45, 260 => 48,
        263 => 51, 266 => 53, 185 or 281 => 56, 284 => 57,
        176 or 293 or 296 => 61, 299 or 302 => 63, 305 or 308 => 65,
        311 => 66, 314 => 67, 179 or 182 or 317 or 323 or 326 => 71,
        227 or 320 or 329 or 332 => 73, 230 or 335 or 338 => 75, 350 => 77,
        353 => 80, 356 => 81, 359 => 82, 362 or 368 or 374 => 85, 365 or 371 or 377 => 86,
        200 or 386 or 389 or 392 or 395 => 95, _ => -1
    };
    public static WeatherSnapshot Parse(JsonElement root, Place place, DateTimeOffset now)
    {
        double Number(JsonElement row, string key) {
            var n = row.GetProperty(key); var value = n.ValueKind == JsonValueKind.String ? double.Parse(n.GetString()!, CultureInfo.InvariantCulture) : n.GetDouble();
            if (!double.IsFinite(value)) throw new JsonException("Invalid weather value"); return value;
        }
        var current = root.GetProperty("current_condition")[0]; var days = root.GetProperty("weather").EnumerateArray().ToArray();
        if (days.Length == 0) throw new JsonException("No weather days");
        DateTime Date(JsonElement row) => DateTime.ParseExact(row.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        TimeZoneInfo? zone = null;
        var zoneId = place.TimeZoneId ?? (place.Country is "中国" or "China" ? "China Standard Time" : null);
        if (zoneId is not null) { try { zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { } }
        if (zone is null && place.Country is "中国" or "China") zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var local = zone is not null ? TimeZoneInfo.ConvertTime(now, zone).DateTime : Date(days[0]);
        if (zone is not null && Date(days[0]) != local.Date) throw new JsonException("Backup daily forecast is stale");
        bool Day(JsonElement row, DateTime at) {
            var sky = row.GetProperty("astronomy")[0];
            return DateTime.TryParse(sky.GetProperty("sunrise").GetString(), CultureInfo.InvariantCulture, out var rise) &&
                DateTime.TryParse(sky.GetProperty("sunset").GetString(), CultureInfo.InvariantCulture, out var set) && at.TimeOfDay >= rise.TimeOfDay && at.TimeOfDay < set.TimeOfDay;
        }
        var hours = days.SelectMany(day => day.GetProperty("hourly").EnumerateArray().Select(h => {
            int hhmm = (int)Number(h, "time"); var time = Date(day).AddHours(hhmm / 100).AddMinutes(hhmm % 100);
            return (time, hour: new WeatherHour(time.ToString("MM/dd HH:mm"), Number(h, "tempC"), ToWmo((int)Number(h, "weatherCode")), Day(day, time)));
        })).Where(h => zone is not null ? h.time > local : h.time.Date > local.Date).Take(72).Select(h => h.hour).ToArray();
        return new(Number(current, "temp_C"), ToWmo((int)Number(current, "weatherCode")), zone is not null && Day(days[0], local),
            Number(days[0], "maxtempC"), Number(days[0], "mintempC"), Number(current, "windspeedKmph"), (int)Number(current, "humidity"), hours, now) {
            Source = "wttr.in", HourResolution = 3, DayPhaseKnown = zone is not null,
            Days = days.Where(d => Date(d).Date > local.Date).Select(d => new WeatherDay(Date(d).ToString("MM/dd"), Number(d, "maxtempC"), Number(d, "mintempC"), ToWmo((int)Number(d.GetProperty("hourly")[4], "weatherCode")))).ToArray()
        };
    }
}
