namespace Mizu.Core.Services;

public enum MemoryDisplayMode { Percentage, Capacity }

public static class MemoryDisplay
{
    public static double? Percentage(long? used, long? total) =>
        used is >= 0 && total is > 0 && used <= total ? 100d * used.Value / total.Value : null;

    public static string Format(long? used, long? total, MemoryDisplayMode mode, double? knownPercentage = null)
    {
        if (mode == MemoryDisplayMode.Percentage)
            return (knownPercentage ?? Percentage(used, total)) is { } percent ? $"{percent:0.#}%" : "—";

        if (used is null && total is not > 0) return "—";
        const long gib = 1024L * 1024 * 1024;
        bool gigabytes = used >= gib || total >= gib;
        double divisor = gigabytes ? gib : 1024 * 1024;
        string FormatBytes(long? value) => value is >= 0 ? (value.Value / divisor).ToString(gigabytes ? "0.#" : "0") : "—";
        return $"{FormatBytes(used)}/{FormatBytes(total is > 0 ? total : null)} {(gigabytes ? "GB" : "MB")}";
    }
}
