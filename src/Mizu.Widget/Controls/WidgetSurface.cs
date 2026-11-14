using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mizu.Core;
using Mizu.Core.Services;

namespace Mizu.Widget.Controls;

public sealed class WidgetSurface(WidgetState state, AppSettings settings) : FrameworkElement
{
    public const double SurfaceWidth = 356, SurfaceHeight = 472;
    public static readonly DependencyProperty FooterRevealProperty = DependencyProperty.Register(nameof(FooterReveal), typeof(double), typeof(WidgetSurface), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double FooterReveal { get => (double)GetValue(FooterRevealProperty); set => SetValue(FooterRevealProperty, value); }
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
            return visible.OrderByDescending(d => d.State == LinkState.Online).ThenByDescending(d => d.State == LinkState.Online && d.Link == LinkKind.Bluetooth).ThenBy(d => deviceOrder.IndexOf(d.Id)).ToArray();
        }
    }
    private Brush foreground = Brushes.Gray, secondary = Brushes.Gray, quiet = Brushes.Gray, green = Brushes.Gray, line = Brushes.Gray;
    private ThemePalette Palette => ThemePalette.Get(settings.Theme);
    public string NetworkHint
    {
        get {
            var n = state.Network;
            var records = n?.Targets ?? [];
            if (records.Length == 0 && !string.IsNullOrWhiteSpace(n?.Target)) records = [new(n.Target,n.Loss,n.Latency,n.Probes)];
            var summaries = records.Select((probe,index) => $"{(settings.ShowNetworkProbeIp ? probe.Target : $"探测 {index+1}")} · {probe.Latency?.ToString() ?? "—"} ms · {probe.Loss?.ToString("0.#") ?? "—"}% · {probe.Probes}/60 次");
            return (n?.Connected == true ? "网卡已连接" : "无活动网卡") + $" · {n?.Adapter}\n↓ 下载 · ↑ 上传\n" + string.Join("\n", summaries) + "\nICMP 被屏蔽不等于实际业务丢包";
        }
    }
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
        var layout = Layout; double height = layout.Height + 44 * FooterReveal, radius = settings.CornerRadius;
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
            if (i > 0 && !(section.Kind == WidgetSection.Todo && !settings.Todos.Any(t => !settings.HideCompletedTodos || !t.Completed))) Art.Line(d, Art.Pen(line, .8), 29, section.Top - 8, 327, section.Top - 8);
            switch (section.Kind)
            {
                case WidgetSection.Weather: DrawWeather(d, section.Top); break;
                case WidgetSection.Devices: DrawDevices(d, section.Top); break;
                case WidgetSection.Metrics: DrawMetrics(d, section.Top); break;
                case WidgetSection.Network: DrawNetwork(d, section.Top); break;
                case WidgetSection.Media:
                    Art.Icon(d, "music", 31, section.Top + 4, 24, secondary);
                    Text(d, string.IsNullOrWhiteSpace(state.Media.Title) ? "—" : state.Media.Title, 68, section.Top, 15, foreground, Dpi, width: 255);
                    Text(d, state.Media.Artist, 68, section.Top + 26, 11, secondary, Dpi, width: 255);
                    break;
                case WidgetSection.Poetry:
                    var quote = state.Poetry is { } poem ? "“" + poem.Content.Replace("\r", " ").Replace("\n", " ").Trim() + "”" : "—";
                    var measure = new FormattedText(quote, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI, Microsoft YaHei UI"), 15 * settings.TextScale, foreground, Dpi);
                    var size = Math.Max(11, Math.Min(15, 15 * 294 / Math.Max(1, measure.WidthIncludingTrailingWhitespace)));
                    Text(d, quote, 31, section.Top + 4, size, state.PoetryFailed ? secondary : foreground, Dpi, width: 294);
                    var credit = state.Poetry is { } source ? $"— {source.DynastyMark} · 《{source.Origin}》" : "—";
                    Art.Text(d, credit, 31, section.Top + 38, 11 * settings.TextScale, secondary, Dpi, width: 294,
                        weight: settings.TextWeight, alignment: TextAlignment.Right);
                    break;
                case WidgetSection.Todo:
                    break;
            }
        }
        if (settings.ShowTodo && FooterReveal > 0) {
            d.PushOpacity(FooterReveal); Art.Line(d, Art.Pen(line, .7), 31, layout.Height - 12, 325, layout.Height - 12); d.Pop();
        }
        d.Pop();
    }
    private void DrawNetwork(DrawingContext d, double y)
    {
        var n = state.Network;
        d.DrawEllipse(n?.Connected == true ? green : quiet, null, new(37, y + 14), 4, 4);
        Text(d, "↓ " + Speed(n?.Download), 52, y + 4, 14, foreground, Dpi, width: 132);
        Text(d, "↑ " + Speed(n?.Upload), 195, y + 4, 14, foreground, Dpi, width: 132);
        var targets = settings.NetworkProbeTargets?.ToArray() ?? new[] { settings.NetworkProbeTarget };
        for (int i = 0; i < targets.Length; i++) {
            var row = y + 38 + i * (settings.ShowNetworkProbeIp ? 44 : 28);
            var probe = n?.Targets.FirstOrDefault(p => p.Target == targets[i]);
            if (probe is null && n?.Target == targets[i] && n.Targets.Length == 0) probe = new(n.Target,n.Loss,n.Latency,n.Probes);
            if (settings.ShowNetworkProbeIp) { Text(d, targets[i], 31, row, 10, quiet, Dpi, width: 294); row += 15; }
            d.DrawEllipse(probe?.Latency is not null ? green : quiet, null, new(37,row+9), 2.5, 2.5);
            Text(d, probe?.Latency is { } ms ? $"{ms} ms" : "— ms", 52, row, 12, secondary, Dpi, width: 135);
            Text(d, probe?.Loss is { } loss ? $"{loss:0.#}%" : "—%", 238, row, 12, secondary, Dpi, width: 85);
        }
    }
    private static string Speed(double? value) => value is not { } v ? "—" : v >= 1048576 ? $"{v / 1048576:0.0} MiB/s" : $"{v / 1024:0.0} KiB/s";
    private void DrawWeather(DrawingContext d, double y)
    {
        var w = state.Weather;
        if (settings.WeatherCurrent || settings.WeatherTodayRange)
        {
            var compact = settings.CompactMode;
            if (settings.WeatherCurrent)
            {
                Text(d, w is null ? "—" : $"{w.Celsius:0}°", 29, y - 4, compact ? 48 : 62, state.WeatherFailed || state.WeatherCached ? secondary : foreground, Dpi);
                if (settings.WeatherTodayRange) Text(d, w is null ? "↑ —   ↓ —" : $"↑ {w.High:0}°   ↓ {w.Low:0}°", 32, y + (compact ? 55 : 73), 12, secondary, Dpi);
            }
            else Text(d, w is null ? "— / —" : $"{w.Low:0}° / {w.High:0}°", 30, y + 16, compact ? 28 : 34, foreground, Dpi);
            if (w is { DayPhaseKnown: false, Code: >= 0 and <= 2 }) Art.Icon(d, "unknown", 261, y + 10, 44, secondary);
            else Art.Weather(d, w?.Code ?? 2, w?.IsDay ?? true, 243, y + 2, compact ? 64 : 80, w is null ? quiet : secondary, Dark);
            if (state.WeatherFailed) d.DrawEllipse(Art.Brush("#B5A276"), null, new(322, y + 9), 3, 3);
            y += compact ? 84 : 116;
        }
        if (settings.WeatherHourly)
        {
            var available = w?.Hours ?? [];
            if (settings.HourlyIncludeCurrent && w?.CurrentHour is { } current) available = new[] { current }.Concat(available).ToArray();
            var hours = available.Where((_, index) => index % Math.Max(1, (int)Math.Ceiling(settings.HourStep / (double)(w?.HourResolution ?? 1))) == 0).Take(settings.ForecastHours).ToArray();
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
        if (area?.Kind == WidgetSection.Weather) tip = (settings.Location is null ? "双击设置选择城市" : settings.Location.ToString()) + (state.Weather is { } w ? $"\n{w.Humidity}% · {w.WindKph:0} km/h\n更新 {w.UpdatedAt.ToLocalTime():HH:mm} · {w.Source}" + (w.HourResolution > 1 ? $"\n备用数据：每 {w.HourResolution} 小时一段，可用天数有限" : "") : "\n天气尚未就绪") + (state.WeatherCached ? "\n启动缓存 · 正在联网刷新" : "") + (state.WeatherFailed ? "\n更新失败，30 秒后重试；当前可能为上次读数" : "");
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
        if (area?.Kind == WidgetSection.Network) tip = NetworkHint;
        if (area?.Kind == WidgetSection.Media) tip = $"{state.Media.Title}\n{state.Media.Artist}\n{state.Media.Status}";
        if (area?.Kind == WidgetSection.Poetry) tip = state.Poetry is { } poem ? $"{poem.Content}\n{poem.Dynasty} · {poem.Author} · 《{poem.Origin}》\n{poem.Source} · {(poem.Source == "内置公版诗库" ? "选取" : "更新")} {poem.FetchedAt.ToLocalTime():MM/dd HH:mm}" + (state.PoetryFailed ? poem.Source == "内置公版诗库" ? "\n在线暂不可用，正在使用内置诗库" : "\n在线更新失败，保留已缓存诗词" : "") : "诗词尚未就绪，可在设置中刷新";
        HoverHints.Set(this, tip, System.Windows.Controls.Primitives.PlacementMode.Mouse);
        base.OnMouseMove(e);
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    { HoverHints.Close(this); base.OnMouseLeave(e); }
}

public sealed class IconView(string name, Brush brush, double stroke = 1.2) : FrameworkElement
{
    protected override void OnRender(DrawingContext drawingContext) => Art.Icon(drawingContext, name, 0, 0, Math.Min(ActualWidth, ActualHeight), brush, stroke);
}
