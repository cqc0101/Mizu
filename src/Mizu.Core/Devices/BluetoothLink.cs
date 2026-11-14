using System.Text.RegularExpressions;
using Mizu.Core.Native;
namespace Mizu.Core.Devices;

public sealed record BluetoothLink(string Id, string Name, string? ContainerId, string? Address, bool? Connected, PeripheralKind Kind)
{
    public static string? NormalizeAddress(string? address) {
        var value = Regex.Replace(address ?? "", "[^0-9A-Fa-f]", "").ToUpperInvariant();
        return value.Length == 12 ? value : null;
    }
    public bool Matches(PnpNode node) => (!string.IsNullOrWhiteSpace(ContainerId) && ContainerId != Guid.Empty.ToString() && string.Equals(ContainerId,node.ContainerId,StringComparison.OrdinalIgnoreCase)) ||
        (NormalizeAddress(Address) is { } mac && node.InstanceId.Contains("DEV_" + mac, StringComparison.OrdinalIgnoreCase));
    public bool Matches(Peripheral device) => (!string.IsNullOrWhiteSpace(ContainerId) && ContainerId != Guid.Empty.ToString() && string.Equals(ContainerId, device.ContainerId, StringComparison.OrdinalIgnoreCase)) ||
        (NormalizeAddress(Address) is { } mac && mac == NormalizeAddress(device.BluetoothAddress));
}
