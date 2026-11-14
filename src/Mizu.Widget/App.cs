using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mizu.Core.Services;
using Mizu.Core;
using Mizu.Widget.Controls;

namespace Mizu.Widget;

public sealed class App : Application
{
    private Mutex? singleInstance;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowW(string? className, string title);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] is "--scan" or "--diagnose")
            {
                var report = Task.Run(async () => { var service = new BatteryService(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); var devices = await service.ScanAsync(timeout.Token); return service.Report(devices); }).GetAwaiter().GetResult();
                var path = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })); return 0;
            }
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (args.Length == 2 && args[0] == "--render-themes") { PreviewRenderer.RenderThemes(args[1]); return 0; }
            if (args.Length == 2 && args[0] == "--render-preview") { PreviewRenderer.Render(args[1]); return 0; }
            app.DispatcherUnhandledException += (_, e) => { AppLog.Write("ui.unhandled", e.Exception.GetType().Name); };
            if (args.Length == 2 && args[0] == "--smoke") return app.Smoke(args[1]);
            using var identity = WindowsIdentity.GetCurrent();
            app.singleInstance = new(false, "Local\\Mizu." + identity.User?.Value);
            bool acquired;
            try { acquired = app.singleInstance.WaitOne(args.Contains("--restart") ? 12000 : 0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
            {
                var hwnd = FindWindowW(null, "Mizu"); if (hwnd != IntPtr.Zero) PostMessageW(hwnd, 0x8001, IntPtr.Zero, IntPtr.Zero);
                app.singleInstance.Dispose(); return 0;
            }
            try
            {
                var window = new MainWindow(new SettingsStore().Load()); app.MainWindow = window;
                window.Show(); return app.Run();
            }
            finally { app.singleInstance.ReleaseMutex(); app.singleInstance.Dispose(); }
        }
        catch (Exception ex) { AppLog.Write("fatal", ex.ToString().Split('\n')[0]); return 1; }
    }
    private int Smoke(string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var window = new MainWindow(new AppSettings { ShowNetwork = true, ShowMedia = true, ShowPoetry = true, Location = new SettingsStore().Load().Location }, persistSettings: false); MainWindow = window;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            try
            {
                PreviewRenderer.SaveVisual((Visual)window.Content, Path.Combine(directory, "live-widget.png"), window.ActualWidth, window.ActualHeight, 2);
                var settings = new SettingsWindow(window); var panel = (FrameworkElement)settings.Content; settings.Content = null; panel.Margin = new(0);
                panel.Measure(new(732, 650)); panel.Arrange(new(0, 0, 732, 650)); panel.UpdateLayout();
                PreviewRenderer.SaveVisual(panel, Path.Combine(directory, "settings.png"), 732, 650, 1.5);
                var tabControl = ((System.Windows.Controls.Grid)panel).Children.OfType<System.Windows.Controls.TabControl>().Single();
                for (int tab = 1; tab < tabControl.Items.Count; tab++)
                {
                    tabControl.SelectedIndex = tab; panel.UpdateLayout();
                    PreviewRenderer.SaveVisual(panel, Path.Combine(directory, $"settings-{tab}.png"), 732, 650, 1.5);
                }
                settings.Close();
                File.WriteAllText(Path.Combine(directory, "smoke.json"), JsonSerializer.Serialize(new
                { window.State.System, window.State.Devices, Bluetooth = window.Engine.BluetoothSummary, window.State.Network, window.State.Media, window.State.Weather, window.State.WeatherCached, window.State.WeatherFailed, window.State.Poetry, window.State.PoetryFailed, window.State.Scanning, Width = window.ActualWidth, Height = window.ActualHeight }, new JsonSerializerOptions { WriteIndented = true }));
                await window.QuitAsync();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "smoke-error.txt"), ex.ToString()); await window.QuitAsync(); }
        };
        window.Show(); timer.Start(); return Run();
    }
}

internal static class PreviewRenderer
{
    public static void SaveVisual(Visual visual, string path, double width, double height, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var file = File.Create(path); encoder.Save(file);
    }
    public static void RenderThemes(string path)
    {
        var scene = new DrawingVisual();
        using (var d = scene.RenderOpen())
        {
            int i = 0;
            foreach (var id in new[] { "navy", "pink", "lavender" })
            {
                var p = ThemePalette.Get(id); var s = new AppSettings { Theme = id };
                var surface = new WidgetSurface(WidgetState.Preview(), s) { Width = 356, Height = WidgetLayout.Calculate(s).Height, PreviewMaterial = true };
                surface.Measure(new(356, surface.Height)); surface.Arrange(new(0, 0, 356, surface.Height)); surface.UpdateLayout();
                d.DrawRectangle(Art.Brush(p.Dark ? "#34445D" : id == "pink" ? "#E9D8DF" : "#DED7EB"), null, new(i * 440, 0, 440, 750));
                Art.Text(d, p.Name, i * 440 + 42, 36, 24, Art.Brush(p.Text));
                Art.Text(d, "主题预览 · 示例数据", i * 440 + 43, 78, 12, Art.Brush(p.Secondary));
                d.DrawRectangle(new VisualBrush(surface), null, new(i * 440 + 42, 142, 356, surface.Height)); i++;
            }
        }
        SaveVisual(scene, path, 1320, 750, 1.5);
    }
    public static void Render(string path)
    {
        var state = WidgetState.Preview();
        for (int i = 0; i < 40; i++) { state.CpuHistory.Enqueue(16 + Math.Sin(i * .6) * 7 + (i % 11 == 0 ? 13 : 0)); state.MemoryHistory.Enqueue(39 + Math.Sin(i * .15) * 3); }
        var configs = new[] {
            new AppSettings { CompactMode = true, WeatherHourly = false, WeatherTodayRange = false, ShowTemperature = false, CornerRadius = 20 },
            new AppSettings { TextScale = 1.25, TextWeight = 600, WeatherDaily = true, ForecastDays = 3, ShowTodo = true, Todos = [new() { Text = "整理桌面" }, new() { Text = "阅读二十分钟" }, new() { Text = "完成今天的设计" }] },
            new AppSettings { Theme = "dark", ShowTodo = true, CornerRadius = 42, Todos = [new() { Text = "散步，看一会儿天空" }, new() { Text = "给键盘充电" }] }
        };
        var scene = new DrawingVisual();
        using (var d = scene.RenderOpen())
        {
            d.DrawRectangle(new LinearGradientBrush(Art.Brush("#EEEBE1").Color, Art.Brush("#B5C2B2").Color, 35), null, new(0, 0, 880, 940));
            d.DrawRectangle(new LinearGradientBrush(Art.Brush("#3F504A").Color, Art.Brush("#172823").Color, 85), null, new(880, 0, 440, 940));
            Art.Text(d, "Mizu", 46, 36, 28, Art.Brush("#5D6B57"));
            Art.Text(d, "自定义外观预览 · 示例数据", 47, 80, 12, Art.Brush("#89917E"));
            var names = new[] { "精简", "天气 · 外设 · 负载 · 待办", "墨绿" };
            for (int i = 0; i < configs.Length; i++)
            {
                var window = new MainWindow(configs[i], state, preview: true, persistSettings: false);
                window.Show(); window.UpdateLayout();
                var content = window.VisualContent;
                var height = WidgetLayout.Calculate(configs[i]).Height;
                var scale = Math.Min(1, 700 / height); double width = 356 * scale, x = i * 440 + (440 - width) / 2;
                for (int j = 16; j > 0; j--) d.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(2, 14, 26, 14)), null, new(x - j / 2.0, 150 - j / 2.0 + 8, width + j, height * scale + j), configs[i].CornerRadius * scale + j / 2.0, configs[i].CornerRadius * scale + j / 2.0);
                d.DrawRectangle(new VisualBrush(content), null, new(x, 150, width, height * scale));
                Art.Text(d, names[i], i * 440 + 46, 885, 12, Art.Brush(i == 2 ? "#AABBA7" : "#72846B"));
                window.Hide();
            }
        }
        SaveVisual(scene, path, 1320, 940, 1.5);
    }
}
