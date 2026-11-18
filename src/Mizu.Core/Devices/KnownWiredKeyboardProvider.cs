using Mizu.Core.Native;
using System.Text.RegularExpressions;

namespace Mizu.Core.Devices;

public sealed class KnownWiredKeyboardProvider(IReadOnlyList<HidEndpoint> endpoints, IReadOnlyList<PnpNode> nodes) : IBatteryProvider
{
    public string Name => "USB keyboard identity";
    private static readonly string[] Brands = ["HHKB", "Keychron", "Logitech", "Razer", "Ducky", "CHERRY", "FILCO", "Leopold", "Varmilo", "SteelSeries", "Corsair", "HyperX", "NuPhy", "Wooting"];
    public static string? RecognizedModel(string product, string manufacturer)
    {
        var name = product.Trim();
        if (name.Length < 5 || name.Length > 100 || name.Any(char.IsControl) ||
            Regex.IsMatch(name, @"receiver|dongle|wireless|bluetooth|virtual|\bvhf\b|\bhid\b|\bdevice\b|vid_|pid_|[\\/]|接收器|无线|蓝牙|虚拟", RegexOptions.IgnoreCase)) return null;
        var brand = Brands.FirstOrDefault(b => name.StartsWith(b + " ", StringComparison.OrdinalIgnoreCase));
        if (brand is null && !string.IsNullOrWhiteSpace(manufacturer) && manufacturer.Length >= 2 &&
            !Regex.IsMatch(manufacturer, @"^(USB|HID|Unknown|Generic|Standard|Manufacturer)$", RegexOptions.IgnoreCase) &&
            name.StartsWith(manufacturer.Trim() + " ", StringComparison.OrdinalIgnoreCase)) brand = manufacturer.Trim();
        if (brand is null) return null;
        var model = Regex.Replace(name[brand.Length..], @"\b(usb|wired|gaming|mechanical|keyboard|standard|input)\b|键盘|有线|机械|游戏|[^\p{L}\p{N}]", "", RegexOptions.IgnoreCase);
        return model.Length >= 2 ? name : null;
    }
    public Task<IReadOnlyList<Peripheral>> ScanAsync(CancellationToken token)
    {
        var results = new List<Peripheral>();
        foreach (var endpoint in endpoints.Where(e => e.UsagePage == 1 && e.Usage is 6 or 7))
        {
            token.ThrowIfCancellationRequested();
            var node = nodes.FirstOrDefault(n => n.InstanceId.Equals(endpoint.InstanceId, StringComparison.OrdinalIgnoreCase));
            if (node is null || !node.ParentId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)) continue;
            if (RecognizedModel(endpoint.Product, endpoint.Manufacturer) is not { } model) continue;
            if (endpoints.Any(e => DeviceIdentity.Physical(e) == DeviceIdentity.Physical(endpoint) &&
                (e.Product.Contains("receiver", StringComparison.OrdinalIgnoreCase) || e.Product.Contains("dongle", StringComparison.OrdinalIgnoreCase)))) continue;
            results.Add(new Peripheral("hid:" + DeviceIdentity.Hash(DeviceIdentity.Physical(endpoint)), model, PeripheralKind.Keyboard,
                LinkKind.Wired, LinkState.Online, null, null, Name, null, endpoint.ContainerId,
                "USB 产品描述提供型号；有线键盘不提供电量") { IdentifiedWiredKeyboard = true, ConnectionObservedAt = DateTimeOffset.UtcNow });
        }
        return Task.FromResult<IReadOnlyList<Peripheral>>(results.DistinctBy(d => d.Id).ToArray());
    }
}
