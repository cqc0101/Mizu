using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Mizu.Core.Services;

namespace Mizu.Widget;

internal static class NativeWindow
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLong64(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLong64(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern IntPtr RegisterDeviceNotificationW(IntPtr hwnd, IntPtr filter, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnregisterDeviceNotification(IntPtr notification);
    internal static IntPtr WatchHid(IntPtr hwnd) {
        var data = Marshal.AllocHGlobal(32);
        try {
            Marshal.Copy(new byte[32],0,data,32); Marshal.WriteInt32(data,32);Marshal.WriteInt32(data,4,5);
            Marshal.StructureToPtr(new Guid("4d1e55b2-f16f-11cf-88cb-001111000030"),data+12,false);
            return RegisterDeviceNotificationW(hwnd,data,0);
        } finally { Marshal.FreeHGlobal(data); }
    }
    public static void Apply(Window window, AppSettings settings)
    {
        var hwnd = new WindowInteropHelper(window).Handle; if (hwnd == IntPtr.Zero) return;
        var style = IntPtr.Size == 8 ? GetWindowLong64(hwnd, -20).ToInt64() : GetWindowLong32(hwnd, -20);
        style |= 0x80; // Tool window, hidden from Alt+Tab.
        style = settings.ClickThrough ? style | 0x20 : style & ~0x20;
        if (IntPtr.Size == 8) SetWindowLong64(hwnd, -20, new(style)); else SetWindowLong32(hwnd, -20, (int)style);
        // This is a per-pixel alpha WPF window. A native accent backdrop paints
        // a separate rectangular surface which can escape both WPF's clip and
        // SetWindowRgn. Keep every material layer in the alpha-clipped visual tree.
    }
    public static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("Mizu", "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue("Mizu", false);
    }
}
