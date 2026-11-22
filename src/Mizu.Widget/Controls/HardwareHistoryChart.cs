using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mizu.Core.Services;

namespace Mizu.Widget.Controls;

public static class HardwareMetricInfo
{
    public static string Title(HardwareMetric metric) => metric switch {
        HardwareMetric.Cpu => "CPU 负载", HardwareMetric.Memory => "内存占用",
        HardwareMetric.CpuTemperature => "CPU 温度", HardwareMetric.GpuDedicated => "专用显存",
        HardwareMetric.VramTemperature => "显存温度", _ => "共享 GPU 内存"
    };
    public static bool IsBytes(HardwareMetric metric) => metric is HardwareMetric.GpuDedicated or HardwareMetric.GpuShared;
    public static bool IsTemperature(HardwareMetric metric) => metric is HardwareMetric.CpuTemperature or HardwareMetric.VramTemperature;
    public static (string Value, string Unit) Format(HardwareMetric metric, double? value)
    {
        string unit = IsBytes(metric) ? "MB" : IsTemperature(metric) ? "°C" : "%";
        if (value is not { } v) return ("—", unit);
        if (IsBytes(metric)) return v >= 1073741824 ? ((v / 1073741824).ToString("0.00", CultureInfo.CurrentCulture), "GB")
            : ((v / 1048576).ToString("0", CultureInfo.CurrentCulture), "MB");
        return (v.ToString("0.0", CultureInfo.CurrentCulture), unit);
    }
    public static bool Enabled(HardwareMetric metric, AppSettings settings) => metric switch {
        HardwareMetric.Cpu => settings.ShowCpu, HardwareMetric.Memory => settings.ShowMemory,
        HardwareMetric.CpuTemperature => settings.ShowTemperature, HardwareMetric.VramTemperature => settings.ShowVramTemperature, _ => settings.ShowGpuMemory
    };
}

public sealed class HardwareHistoryChart : FrameworkElement
{
    public HardwareMetric Metric { get; private set; }
    public IReadOnlyList<HardwareSample> Samples { get; private set; } = [];
    public TimeSpan Range { get; private set; } = TimeSpan.FromMinutes(15);
    public DateTimeOffset Now { get; private set; }
    public ThemePalette Palette { get; private set; } = ThemePalette.All[0];
    private double? pointer;

    public void Update(HardwareMetric metric, IReadOnlyList<HardwareSample> samples, DateTimeOffset now, TimeSpan range, ThemePalette palette)
    {
        Metric = metric; Samples = samples; Now = now; Range = range; Palette = palette;
        System.Windows.Automation.AutomationProperties.SetName(this, HardwareMetricInfo.Title(metric) + "历史折线图");
        InvalidateVisual();
    }
    protected override void OnMouseMove(MouseEventArgs e)
    { pointer = e.GetPosition(this).X; InvalidateVisual(); base.OnMouseMove(e); }
    protected override void OnMouseLeave(MouseEventArgs e)
    { pointer = null; InvalidateVisual(); base.OnMouseLeave(e); }

    protected override void OnRender(DrawingContext d)
    {
        if (ActualWidth < 160 || ActualHeight < 100) return;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var plot = new Rect(51, 28, ActualWidth - 74, ActualHeight - 77);
        var accent = Art.Brush(Palette.Accent); var text = Art.Brush(Palette.Text);
        var secondary = Art.Brush(Palette.Secondary); var quiet = Art.Brush(Palette.Quiet);
        var line = Art.Brush(Palette.Line);
        d.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        void Text(string s, double x, double y, double size, Brush brush, double? width = null, TextAlignment alignment = TextAlignment.Left) =>
            Art.Text(d, s, x, y, size, brush, dpi, width: width, alignment: alignment);
        var valid = Samples.Where(s => s.Value.HasValue).ToArray();
        double max = valid.Length == 0 ? 0 : valid.Max(s => s.Value!.Value);
        double divisor = HardwareMetricInfo.IsBytes(Metric) ? max >= 1073741824 ? 1073741824 : 1048576 : 1;
        var unit = HardwareMetricInfo.IsBytes(Metric) ? divisor == 1073741824 ? "GB" : "MB" : HardwareMetricInfo.IsTemperature(Metric) ? "°C" : "%";
        double ceiling = Metric is HardwareMetric.Cpu or HardwareMetric.Memory ? 100 :
            HardwareMetricInfo.IsTemperature(Metric) ? Math.Max(100, NiceCeiling(max * 1.12)) : NiceCeiling(Math.Max(1, max / divisor * 1.12));
        Text(unit, 2, 6, 10, secondary);
        for (int i = 0; i <= 4; i++) {
            double y = plot.Top + plot.Height * i / 4;
            var pen = Art.Pen(line, .8); pen.DashStyle = i == 4 ? DashStyles.Solid : new DashStyle([3, 5], 0);
            d.DrawLine(pen, new(plot.Left, y), new(plot.Right, y));
            Text((ceiling * (4 - i) / 4).ToString("0.#", CultureInfo.CurrentCulture), 0, y - 7, 10, quiet, 39, TextAlignment.Right);
        }
        for (int i = 0; i <= 4; i++) {
            var time = Now - Range + TimeSpan.FromTicks(Range.Ticks * i / 4);
            double x = plot.Left + plot.Width * i / 4;
            Text(time.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture), x - 26, plot.Bottom + 17, 10, secondary, 52, TextAlignment.Center);
        }
        double X(DateTimeOffset time) => plot.Left + (time - (Now - Range)).TotalSeconds / Range.TotalSeconds * plot.Width;
        double Y(double value) => plot.Bottom - Math.Clamp(value / divisor / ceiling, 0, 1) * plot.Height;
        d.PushClip(new RectangleGeometry(new Rect(plot.Left - 3, plot.Top - 3, plot.Width + 6, plot.Height + 6)));
        var segment = new List<Point>(); DateTimeOffset? previous = null;
        void DrawSegment() {
            if (segment.Count == 0) return;
            if (segment.Count == 1) d.DrawEllipse(accent, null, segment[0], 2.5, 2.5);
            else {
                var geometry = new StreamGeometry();
                using (var g = geometry.Open()) { g.BeginFigure(segment[0], false, false); g.PolyLineTo(segment.Skip(1).ToArray(), true, false); }
                geometry.Freeze();
                var fill = new StreamGeometry();
                using (var g = fill.Open()) {
                    g.BeginFigure(new(segment[0].X, plot.Bottom), true, true);
                    g.PolyLineTo(segment.ToArray(), true, false); g.LineTo(new(segment[^1].X, plot.Bottom), true, false);
                }
                fill.Freeze();
                var c = accent.Color; var top = c; top.A = 35; var bottom = c; bottom.A = 0;
                d.DrawGeometry(new LinearGradientBrush(top, bottom, 90), null, fill);
                d.PushOpacity(.09); d.DrawGeometry(null, Art.Pen(accent, 6), geometry); d.Pop();
                d.DrawGeometry(null, Art.Pen(accent, 1.9), geometry);
            }
            segment.Clear();
        }
        foreach (var sample in Samples) {
            if (sample.Value is null) { DrawSegment(); previous = null; continue; }
            if (sample.StartsSegment || previous is { } at && sample.Time - at > TimeSpan.FromSeconds(12)) DrawSegment();
            segment.Add(new(X(sample.Time), Y(sample.Value.Value))); previous = sample.Time;
        }
        DrawSegment(); d.Pop();
        if (valid.Length == 0) {
            Text("暂无历史数据", plot.Left, plot.Top + plot.Height / 2 - 22, 15, text, plot.Width, TextAlignment.Center);
            Text("开启对应监控后，曲线会在这里逐渐积累", plot.Left, plot.Top + plot.Height / 2 + 7, 11, secondary, plot.Width, TextAlignment.Center);
            return;
        }
        // Leave unavailable readings and sleep/resume gaps visibly empty.
        HardwareSample? selected = null;
        if (pointer is { } px && px >= plot.Left && px <= plot.Right && Samples.Count > 0) {
            var nearest = Samples.MinBy(s => Math.Abs(X(s.Time) - px));
            if (Math.Abs(X(nearest.Time) - px) <= plot.Width * 8 / Range.TotalSeconds) selected = nearest;
        }
        if (selected is { } picked) {
            double x = X(picked.Time);
            d.DrawLine(Art.Pen(line), new(x, plot.Top), new(x, plot.Bottom));
            if (picked.Value is { } value) {
                var point = new Point(x, Y(value));
                d.DrawEllipse(Art.Brush(Palette.Base), Art.Pen(accent, 2), point, 4, 4);
            }
            var formatted = HardwareMetricInfo.Format(Metric, picked.Value);
            string label = picked.Time.ToLocalTime().ToString("HH:mm:ss") + "   " + formatted.Value + " " + formatted.Unit;
            var size = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI, Microsoft YaHei UI"), 11, text, dpi);
            double width = size.Width + 24, left = Math.Clamp(x - width / 2, plot.Left, Math.Max(plot.Left, plot.Right - width));
            d.DrawRoundedRectangle(Palette.HintBackground, Art.Pen(line, .8), new(left, 0, width, 25), 8, 8);
            Text(label, left + 12, 5, 11, text);
        } else if (valid.Last() is var last && Now - last.Time <= TimeSpan.FromSeconds(12)) {
            var point = new Point(X(last.Time), Y(last.Value!.Value));
            d.DrawEllipse(Art.Brush(Palette.Base), Art.Pen(accent, 1.8), point, 3.5, 3.5);
        }
    }
    private static double NiceCeiling(double value)
    {
        double power = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1, value))));
        double scaled = value / power;
        return (scaled <= 1 ? 1 : scaled <= 2 ? 2 : scaled <= 5 ? 5 : 10) * power;
    }
}
