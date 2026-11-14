using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Mizu.Core;
using Mizu.Core.Devices;
using Mizu.Core.Services;

namespace Mizu.Widget;

public sealed class BluetoothMonitor(Action changed) : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, DeviceInformation> devices = new();
    private readonly Dictionary<string, BluetoothLink> links = new();
    private readonly List<DeviceWatcher> watchers = [];
    private volatile bool disposed;
    public IReadOnlyList<BluetoothLink> Snapshot()
    {
        lock(gate) return links.Values.GroupBy(l => BluetoothLink.NormalizeAddress(l.Address) ?? l.ContainerId ?? l.Id)
            .Select(g => {
                var best = g.OrderByDescending(l => l.Connected == true).ThenByDescending(l => l.Kind != PeripheralKind.Unknown).First();
                return best with { Kind = best.Kind != PeripheralKind.Unknown ? best.Kind : g.FirstOrDefault(l => l.Kind != PeripheralKind.Unknown)?.Kind ?? PeripheralKind.Unknown,
                    Connected = g.Any(l => l.Connected == true) ? true : g.All(l => l.Connected == false) ? false : null };
            }).ToArray();
    }
    private void Notify() { if (disposed) return; try { changed(); } catch(Exception ex) { AppLog.Write("bluetooth.notify",ex.GetType().Name); } }
    private static BluetoothLink Read(DeviceInformation device)
    {
        object? Get(string name) => device.Properties.TryGetValue(name, out var value) ? value : null;
        var connected = Get("System.Devices.Aep.IsConnected") is bool value ? value : (bool?)null;
        var id = new[] { Get("System.Devices.ContainerId"), Get("System.Devices.Aep.ContainerId") }.Select(v => Guid.TryParse(v?.ToString(), out var g) && g != Guid.Empty ? g.ToString() : null).FirstOrDefault(v => v is not null);
        var category = Get("System.Devices.Aep.Category") is IEnumerable<string> categories ? string.Join(" ",categories) : "";
        var kind = DeviceClassifier.FromMetadata(category);
        if (ushort.TryParse(Get("System.Devices.Aep.Bluetooth.Le.Appearance")?.ToString(), out var appearance))
            kind = appearance == 0x3c2 ? PeripheralKind.Mouse : appearance == 0x3c1 ? PeripheralKind.Keyboard : kind;
        return new(device.Id, device.Name, id, BluetoothLink.NormalizeAddress(Get("System.Devices.Aep.DeviceAddress")?.ToString()), connected, kind);
    }
    private void Added(DeviceWatcher sender, DeviceInformation device)
    {
        try {
        bool notify;
        lock(gate) { if(disposed)return; var link = Read(device); notify = !links.TryGetValue(device.Id, out var previous) || link != previous; devices[device.Id]=device;links[device.Id]=link; }
        if(notify) Notify();
        } catch(Exception ex) { AppLog.Write("bluetooth.add",ex.GetType().Name); }
    }
    private void Updated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        try {
        bool notify = false;
        lock(gate) { if(disposed)return; if(devices.TryGetValue(update.Id,out var device)) { device.Update(update); var link=Read(device);notify=link!=links[update.Id];links[update.Id]=link; } }
        if(notify) Notify();
        } catch(Exception ex) { AppLog.Write("bluetooth.update",ex.GetType().Name); }
    }
    private void Removed(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        bool notify = false;
        lock(gate) { if(disposed)return; if(links.TryGetValue(update.Id,out var link)) { notify=link.Connected!=false;links[update.Id]=link with { Connected=false };devices.Remove(update.Id); } }
        if(notify) Notify();
    }
    public void Start()
    {
        foreach(var selector in new[] { BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), BluetoothDevice.GetDeviceSelectorFromPairingState(true) })
        {
            try {
                var watcher=DeviceInformation.CreateWatcher(selector, new[] { "System.Devices.Aep.IsConnected", "System.Devices.Aep.ContainerId", "System.Devices.ContainerId", "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.Category", "System.Devices.Aep.Bluetooth.Le.Appearance" }, DeviceInformationKind.AssociationEndpoint);
                watcher.Added+=Added;watcher.Updated+=Updated;watcher.Removed+=Removed;watchers.Add(watcher);watcher.Start();
            } catch(Exception ex) { AppLog.Write("bluetooth.watch",ex.GetType().Name); }
        }
    }
    public void Dispose()
    {
        lock(gate) disposed=true;
        foreach(var watcher in watchers) {
            watcher.Added-=Added;watcher.Updated-=Updated;watcher.Removed-=Removed;
            try { if(watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop(); } catch(Exception ex) { AppLog.Write("bluetooth.stop",ex.GetType().Name); }
        }
    }
}
