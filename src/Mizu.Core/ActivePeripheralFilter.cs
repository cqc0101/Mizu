using System.Text.RegularExpressions;
namespace Mizu.Core;

public static class ActivePeripheralFilter
{
    public static IReadOnlyList<Peripheral> Select(IEnumerable<Peripheral> source)
    {
        var online = source.Where(d => d.State == LinkState.Online).ToArray();
        var selected = online.GroupBy(d => !string.IsNullOrWhiteSpace(d.ContainerId) && d.ContainerId != Guid.Empty.ToString()
                ? $"container:{d.ContainerId}:{d.Kind}" : "id:" + d.Id)
            .Select(Preferred).ToArray();
        // Match channel variants only when the model is specific and each channel
        // has a single candidate. Never collapse multiple devices of the same kind.
        return selected.GroupBy(d => Model(d) ?? "id:" + d.Id).SelectMany(group =>
            group.Select(d => d.Link).Distinct().Count() > 1 && group.GroupBy(d => d.Link).All(g => g.Count() == 1)
                ? new[] { Preferred(group) } : group.ToArray()).ToArray();
    }
    private static Peripheral Preferred(IEnumerable<Peripheral> devices) => devices.OrderByDescending(d => d.Link == LinkKind.Bluetooth).ThenByDescending(d => d.ObservedAt).First();
    private static string? Model(Peripheral device)
    {
        var name = Regex.Replace(device.Name, @"\b(bluetooth|tri[ -]?mode|mouse|keyboard|2\.4\s*g(?:hz)?|蓝牙|鼠标|键盘)\b", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name.ToUpperInvariant(), @"[^\p{L}\p{N}]", "");
        return name.Length >= 5 && name.Any(char.IsDigit) ? $"model:{device.Kind}:{name}" : null;
    }
}
