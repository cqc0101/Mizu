using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mizu.Core;
using Mizu.Core.Services;

namespace Mizu.Widget.Controls;

public sealed class WidgetSurface(WidgetState state, AppSettings settings) : FrameworkElement
{
    public const double SurfaceWidth = 356, SurfaceHeight = 472;
    public bool PreviewMaterial { get; set; }
    public WidgetLayout Layout => WidgetLayout.Calculate(settings);
    private readonly DevicePager pager = new();
    private readonly List<string> deviceOrder = [];
    public IReadOnlyList<Peripheral> VisibleDevices
    {
        get
        {
            var visible = state.Devices.Where(d => !settings.HiddenDevices.Contains(d.Id)).Select(d => d with
    { Name = settings.NameOverrides.GetValueOrDefault(d.Id, d.Name), Kind = settings.TypeOverrides.GetValueOrDefault(d.Id, d.Kind) })
                .OrderByDescending(d => d.State == LinkState.Online).ThenByDescending(d => d.ObservedAt.HasValue).ThenBy(d => d.Kind).ToArray();
            foreach (var device in visible) if (!deviceOrder.Contains(device.Id)) deviceOrder.Add(device.Id);
            return visible.OrderBy(d => deviceOrder.IndexOf(d.Id)).ToArray();
        }
    }
    private Brush foreground = Brushes.Gray, secondary = Brushes.Gray, quiet = Brushes.Gray, green = Brushes.Gray, line = Brushes.Gray;
    private ThemePalette Palette => ThemePalette.Get(settings.Theme);
    private bool Dark => Palette.Dark;
    private double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;
    private void Text(DrawingContext d, string value, double x, double y, double size, Brush brush, double pixelsPerDip = 1, bool medium = false, double? width = null)
    {
        var available = width ?? Math.Max(1, SurfaceWidth - 29 - x);
        Art.Text(d, value, x, y + size * (1 - settings.TextScale) * .5, size * settings.TextScale, brush,
            pixelsPerDip, medium, available, settings.TextWeight, fit: value.All(c => !char.IsLetter(c)));
    }
    protected override void OnRender(DrawingContext d)
    {
        base.OnRender(d);
        foreground = Art.Brush(Palette.Text); secondary = Art.Brush(Palette.Secondary);
        quiet = Art.Brush(Palette.Quiet); green = Art.Brush(Palette.Accent); line = Art.Brush(Palette.Line);
        var layout = Layout; double height = layout.Height, radius = settings.CornerRadius;
        var baseColor = Art.Brush(Palette.Base).Color;
        baseColor.A = PreviewMaterial ? (byte)212 : (byte)(settings.GlassOpacity * 255);
        var glass = new LinearGradientBrush(baseColor, Color.FromArgb((byte)Math.Max(16, baseColor.A - 18), baseColor.R, baseColor.G, baseColor.B), 95);
        d.PushClip(new RectangleGeometry(new(0, 0, SurfaceWidth, height), radius, radius));
        d.DrawRoundedRectangle(glass, null, new(0, 0, SurfaceWidth, height), radius, radius);
        var glow = new RadialGradientBrush(Color.FromArgb(Dark ? (byte)18 : (byte)68, 255, 255, 255), Colors.Transparent)
        { GradientOrigin = new(.1, .0), Center = new(.1, .0), RadiusX = 1.1, RadiusY = .7 };
        d.DrawRectangle(glow, null, new(0, 0, SurfaceWidth, height));
        d.DrawRoundedRectangle(null, Art.Pen(Art.Brush(Dark ? "#38FFFFFF" : "#C8FFFFFF"), 1.1), new(.7, .7, SurfaceWidth - 1.4, height - 1.4), Math.Max(0, radius - .7), Math.Max(0, radius - .7));
        for (int i = 0; i < layout.Sections.Length; i++)
        {
            var section = layout.Sections[i];
            if (i > 0) Art.Line(d, Art.Pen(line, .8), 29, section.Top - 8, 327, section.Top - 8);
            switch (section.Kind)
            {
                case WidgetSection.Weather: DrawWeather(d, section.Top); break;
                case WidgetSection.Devices: DrawDevices(d, section.Top); break;
                case WidgetSection.Metrics: DrawMetrics(d, section.Top); break;
                case WidgetSection.Todo:
                    if (!settings.Todos.Any(t => !settings.HideCompletedTodos || !t.Completed))
                    { d.DrawEllipse(null, Art.Pen(quiet, 1), new(39, section.Top + 16), 6, 6); Art.Line(d, Art.Pen(line), 59, section.Top + 16, 217, section.Top + 16); }
                    break;
            }
        }
        d.Pop();
    }
    private void DrawWeather(DrawingContext d, double y)
    {
        var w = state.Weather;
        if (settings.WeatherCurrent || settings.WeatherTodayRange)
        {
            var compact = settings.CompactMode;
            if (settings.WeatherCurrent)
            {
                Text(d, w is null ? "—" : $"{w.Celsius:0}°", 29, y - 4, compact ? 48 : 62, state.WeatherFailed ? secondary : foreground, Dpi);
                if (settings.WeatherTodayRange) Text(d, w is null ? "↑ —   ↓ —" : $"↑ {w.High:0}°   ↓ {w.Low:0}°", 32, y + (compact ? 55 : 73), 12, secondary, Dpi);
            }
            else Text(d, w is null ? "— / —" : $"{w.Low:0}° / {w.High:0}°", 30, y + 16, compact ? 28 : 34, foreground, Dpi);
            Art.Weather(d, w?.Code ?? 2, w?.IsDay ?? true, 243, y + 2, compact ? 64 : 80, w is null ? quiet : secondary, Dark);
            if (state.WeatherFailed) d.DrawEllipse(Art.Brush("#B5A276"), null, new(322, y + 9), 3, 3);
            y += compact ? 84 : 116;
        }
        if (settings.WeatherHourly)
        {
            var available = w?.Hours ?? [];
            if (settings.HourlyIncludeCurrent && w?.CurrentHour is { } current) available = new[] { current }.Concat(available).ToArray();
            var hours = available.Where((_, index) => index % settings.HourStep == 0).Take(settings.ForecastHours).ToArray();
            for (int i = 0; i < settings.ForecastHours; i++)
            {
                double x = 30 + i % 5 * 61, row = y + i / 5 * 60;
                if (i < hours.Length)
                {
                    var hour = hours[i];
                    Text(d, hour.Time.Length > 5 ? hour.Time[..5] : "", x, row - 2, 8, quiet, Dpi);
                    Text(d, hour.Time.Length > 5 ? hour.Time[^5..] : hour.Time, x, row + 10, 10, quiet, Dpi);
                    Art.Weather(d, hour.Code, hour.IsDay, x - 3, row + 24, 25, secondary, Dark);
                    Text(d, $"{hour.Temperature:0}°", x + 25, row + 29, 12, secondary, Dpi);
                }
                else Text(d, "—", x + 8, row + 21, 14, quiet, Dpi);
            }
            y += Math.Ceiling(settings.ForecastHours / 5.0) * 60;
        }
        if (settings.WeatherDaily)
        {
            var days = w?.Days ?? [];
            for (int i = 0; i < settings.ForecastDays; i++)
            {
                double x = 30 + i % 4 * 77, row = y + i / 4 * 70;
                if (i < days.Length)
                {
                    var day = days[i]; Text(d, day.Date, x, row, 10, quiet, Dpi);
                    Art.Weather(d, day.Code, true, x - 2, row + 14, 31, secondary, Dark);
                    Text(d, $"{day.Low:0}° / {day.High:0}°", x, row + 48, 11, secondary, Dpi);
                }
                else Text(d, "—", x + 10, row + 20, 14, quiet, Dpi);
            }
        }
    }
    private void DrawDevices(DrawingContext d, double top)
    {
        var devices = VisibleDevices; var pages = Math.Max(1, (int)Math.Ceiling(devices.Count / 2.0));
        state.DevicePage = Math.Clamp(state.DevicePage, 0, pages - 1);
        var current = devices.Skip(state.DevicePage * 2).Take(2).ToArray();
        for (int row = 0; row < 2; row++)
        {
            double y = top + row * (settings.CompactMode ? 56 : 78);
            var peripheral = row < current.Length ? current[row] : null;
            var online = peripheral?.State == LinkState.Online; var deviceLine = online ? secondary : quiet;
            var fill = Art.Brush(Dark ? "#0AFFFFFF" : "#18FFFFFF");
            var kind = peripheral?.Kind ?? (row == 0 ? PeripheralKind.Keyboard : PeripheralKind.Mouse);
            d.PushTransform(new TranslateTransform(30, y + 12));
            if (settings.CompactMode) d.PushTransform(new ScaleTransform(.75, .75));
            if (kind == PeripheralKind.Keyboard) Art.Keyboard(d, 0, 0, deviceLine, fill);
            else if (kind == PeripheralKind.Mouse) Art.Mouse(d, 0, 0, deviceLine, fill);
            else Art.Icon(d, "unknown", 21, 0, 32, deviceLine);
            if (settings.CompactMode) d.Pop(); d.Pop();
            var textX = settings.CompactMode ? 105 : 122;
            Text(d, peripheral?.Name ?? "—", textX, y, settings.CompactMode ? 11 : 12, online ? foreground : secondary, Dpi, width: settings.CompactMode ? 155 : 204);
            Text(d, peripheral?.Percent is { } percent ? $"{percent}%" : "—", textX, y + 18, settings.CompactMode ? 18 : 26, online ? foreground : quiet, Dpi);
            if (peripheral?.Charging == true) Art.Icon(d, "bolt", textX + 75, y + 29, 12, green);
            var connection = peripheral?.Link switch { LinkKind.Bluetooth => "bluetooth", LinkKind.Receiver => "receiver", LinkKind.NearLink => "nearlink", LinkKind.Wired => "wired", _ => "" };
            if (settings.ShowConnectionIcons && connection.Length > 0) Art.Icon(d, connection, 288, y + 25, 17, deviceLine, 1.05);
            d.DrawEllipse(online ? green : line, null, new(322, y + 34), 2.8, 2.8);
            if (!settings.CompactMode)
            {
                d.DrawRoundedRectangle(line, null, new(textX, y + 59, 204, 2.5), 1.25, 1.25);
                if (peripheral?.Percent is { } pct) d.DrawRoundedRectangle(online ? pct <= settings.BatteryThreshold ? Art.Brush("#B39B70") : green : quiet, null, new(textX, y + 59, 204.0 * pct / 100, 2.5), 1.25, 1.25);
            }
        }
        if (pages > 1) for (var i = 0; i < Math.Min(pages, 10); i++)
            d.DrawEllipse(i == state.DevicePage ? secondary : line, null, new(178 + (i - (Math.Min(pages, 10) - 1) / 2.0) * 8, top + (settings.CompactMode ? 110 : 151)), 1.6, 1.6);
    }
    private (string Icon, double? Value, string Tip)[] Metrics => new[] {
        ("cpu", state.System.CpuPercent, "CPU 总负载 · 最近 80 秒", settings.ShowCpu),
        ("memory", state.System.MemoryPercent, "物理内存占用 · 最近 80 秒", settings.ShowMemory),
        ("temperature", state.System.CpuCelsius, state.System.TemperatureStatus, settings.ShowTemperature)
    }.Where(v => v.Item4).Select(v => (v.Item1, v.Item2, v.Item3)).ToArray();
    private void DrawMetrics(DrawingContext d, double top)
    {
        var metrics = Metrics;
        for (int i = 0; i < metrics.Length; i++)
        {
            double column = 300.0 / metrics.Length, x = 30 + i * column;
            var m = metrics[i]; Art.Icon(d, m.Icon, x, top + 13, 17, secondary, 1.05);
            Text(d, m.Value is { } v ? $"{v:0}" + (m.Icon == "temperature" ? "°" : "%") : "—", x + 25, top + 3, 24, foreground, Dpi, width: column - 25);
            if (settings.ShowGraphs && !settings.CompactMode)
            {
                if (m.Icon != "temperature") Sparkline(d, m.Icon == "cpu" ? state.CpuHistory.ToArray() : state.MemoryHistory.ToArray(), x + 1, top + 50, column - 25, 15, green, line);
                else for (int j = 0; j < 16; j++) d.DrawRoundedRectangle(m.Value is { } temp && j < temp / 100 * 16 ? green : line, null, new(x + j * (column - 25) / 16, top + 53, 2, 8), 1, 1);
            }
        }
    }
    private static void Sparkline(DrawingContext d, double[] values, double x, double y, double w, double h, Brush stroke, Brush faint)
    {
        if (values.Length < 2) { Art.Line(d, Art.Pen(faint, 1), x, y + h * .7, x + w, y + h * .7); return; }
        var points = values.Select((v, i) => new Point(x + w * i / Math.Max(1, values.Length - 1), y + h * (1 - v / 100))).ToArray();
        var geometry = new StreamGeometry(); using (var c = geometry.Open()) { c.BeginFigure(points[0], false, false); c.PolyLineTo(points.Skip(1).ToArray(), true, true); }
        d.DrawGeometry(null, Art.Pen(stroke, 1), geometry);
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var point = e.GetPosition(this); var area = Layout.Sections.FirstOrDefault(s => s.Kind == WidgetSection.Devices);
        if (area is not null && point.Y >= area.Top && point.Y <= area.Top + area.Height && VisibleDevices.Count > 2)
        {
            var next = pager.Advance(state.DevicePage, e.Delta, VisibleDevices.Count, Environment.TickCount64);
            if (next != state.DevicePage) { state.DevicePage = next; HoverHints.Set(this, null); InvalidateVisual(); }
            e.Handled = true;
        }
        base.OnMouseWheel(e);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this); var area = Layout.Sections.FirstOrDefault(s => p.Y >= s.Top && p.Y <= s.Top + s.Height);
        string? tip = null;
        if (area?.Kind == WidgetSection.Weather) tip = (settings.Location is null ? "双击设置选择城市" : settings.Location.ToString()) + (state.Weather is { } w ? $"\n{w.Humidity}% · {w.WindKph:0} km/h\n更新 {w.UpdatedAt.ToLocalTime():HH:mm} · Open-Meteo" : "\n天气尚未就绪") + (state.WeatherFailed ? "\n当前为上次天气" : "");
        if (area?.Kind == WidgetSection.Devices)
        {
            var index = (p.Y - area.Top < (settings.CompactMode ? 56 : 78) ? 0 : 1) + state.DevicePage * 2;
            var devices = VisibleDevices;
            if (index < devices.Count)
            {
                var device = devices[index];
                var connection = device.Link switch { LinkKind.Bluetooth => "蓝牙", LinkKind.Receiver => "无线接收器", LinkKind.NearLink => "星闪", LinkKind.Wired => "有线", _ => "连接待确认" };
                var stateText = device.State switch { LinkState.Online => "已连接", LinkState.Offline => "未连接 · 上次读数", _ => "状态待确认" };
                tip = $"{device.Name}\n{connection} · {stateText}";
            }
            else tip = "打开设置查看外设检测";
            if (devices.Count > 2) tip += "\n滚轮切换设备";
        }
        if (area?.Kind == WidgetSection.Metrics && Metrics.Length > 0) tip = Metrics[Math.Clamp((int)((p.X - 30) / (300.0 / Metrics.Length)), 0, Metrics.Length - 1)].Tip;
        HoverHints.Set(this, tip, System.Windows.Controls.Primitives.PlacementMode.Mouse);
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    { HoverHints.Close(this); base.OnMouseLeave(e); }
}

public sealed class IconView(string name, Brush brush) : FrameworkElement
{
    protected override void OnRender(DrawingContext drawingContext) => Art.Icon(drawingContext, name, 0, 0, Math.Min(ActualWidth, ActualHeight), brush, 1.2);
}
