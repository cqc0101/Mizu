using System.Text.Json.Serialization;

namespace Mizu.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PeripheralKind { Unknown, Keyboard, Mouse }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LinkKind { Unknown, Bluetooth, Receiver, Wired, NearLink }
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LinkState { Unknown, Online, Offline }

public sealed record Peripheral(
    string Id, string Name, PeripheralKind Kind, LinkKind Link, LinkState State,
    int? Percent, bool? Charging, string Source, DateTimeOffset? ObservedAt,
    string? ContainerId = null, string? Detail = null)
{
    public bool IsFresh(DateTimeOffset now) => ObservedAt is { } at && now >= at && now - at < TimeSpan.FromMinutes(3);
}

public sealed record SystemSnapshot(double? CpuPercent, double? MemoryPercent, double? CpuCelsius, string TemperatureStatus);
public sealed record WeatherHour(string Time, double Temperature, int Code, bool IsDay);
public sealed record WeatherDay(string Date, double High, double Low, int Code);
public sealed record WeatherSnapshot(double Celsius, int Code, bool IsDay, double High, double Low,
    double WindKph, int Humidity, WeatherHour[] Hours, DateTimeOffset UpdatedAt)
{
    public WeatherHour? CurrentHour { get; init; }
    public WeatherDay[] Days { get; init; } = [];
}
public sealed record Place(string Name, string Country, string? Region, double Latitude, double Longitude)
{
    public override string ToString() => string.Join(" · ", new[] { Name, Region, Country }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct());
}

public interface IBatteryProvider
{
    string Name { get; }
    Task<IReadOnlyList<Peripheral>> ScanAsync(CancellationToken cancellationToken);
}

public static class DeviceClassifier
{
    public static PeripheralKind FromHid(ushort page, ushort usage) => page == 1
        ? usage switch { 6 or 7 => PeripheralKind.Keyboard, 2 => PeripheralKind.Mouse, _ => PeripheralKind.Unknown }
        : PeripheralKind.Unknown;
    public static PeripheralKind FromLogitech(byte type) => type switch
    { 0 or 2 => PeripheralKind.Keyboard, 3 or 4 or 5 => PeripheralKind.Mouse, _ => PeripheralKind.Unknown };
    public static PeripheralKind FromMetadata(string? value)
    {
        var v = (value ?? "").ToLowerInvariant();
        if (v.Contains("input.keyboard") || v.Contains("keyboard") || v.Contains("键盘")) return PeripheralKind.Keyboard;
        if (v.Contains("input.mouse") || v.Contains("mouse") || v.Contains("鼠标")) return PeripheralKind.Mouse;
        return PeripheralKind.Unknown;
    }
}
