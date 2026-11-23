using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Mizu.Core;
using Mizu.Core.Services;

namespace Mizu.Widget.Controls;

public sealed class WidgetSurface(WidgetState state, AppSettings settings) : FrameworkElement
{
    public const double SurfaceWidth = WidgetLayout.BaseWidth, SurfaceHeight = 472;
    public static readonly DependencyProperty FooterRevealProperty = DependencyProperty.Register(nameof(FooterReveal), typeof(double), typeof(WidgetSurface), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double FooterReveal { get => (double)GetValue(FooterRevealProperty); set => SetValue(FooterRevealProperty, value); }
    public double FooterHeight { get; set; } = 44;
    public static readonly DependencyProperty WeatherProgressProperty = DependencyProperty.Register("WeatherProgress", typeof(double), typeof(WidgetSurface), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    private DrawingGroup? weatherFrame, weatherFrom;
    private string? weatherConfiguration;
    private int weatherGeneration;
    public bool PreviewMaterial { get; set; }
    public double WorkAreaHeight { get; set; } = double.PositiveInfinity;
    public double WorkAreaWidth { get; set; } = double.PositiveInfinity;
    public WidgetLayout TargetLayout => WidgetLayout.Calculate(settings, VisibleDevices.Count, WorkAreaHeight, WorkAreaWidth);
    public WidgetLayout? TransitionLayout { get; set; }
    public WidgetLayout Layout => TransitionLayout ?? TargetLayout;
    public double SectionInset => Math.Max(0, (Width - SurfaceWidth) / 2);
    private readonly PeripheralPresence presence = new();
    private readonly DevicePager pager = new();
    private readonly VocabularyRotation vocabulary = new();
    public static readonly DependencyProperty VocabularyOffsetProperty = DependencyProperty.Register("VocabularyOffset", typeof(double),
        typeof(WidgetSurface), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private double VocabularyOffset => (double)GetValue(VocabularyOffsetProperty);
    private double vocabularyScrollTarget;
    private int vocabularyScrollGeneration;
    private IReadOnlyList<VocabularyEntry>? drawnVocabularyOrder;
    private int drawnVocabularyColumns = 1;
    private readonly List<string> deviceOrder = [];
    public IReadOnlyList<Peripheral> VisibleDevices
    {
        get
        {
            var candidates = presence.Update(state.Devices, DateTimeOffset.UtcNow).Where(d => !settings.HiddenDevices.Contains(d.Id) && (!d.IdentifiedWiredKeyboard || settings.ShowWiredKeyboards)).ToArray();
            var active = settings.HideInactiveDevices ? ActivePeripheralFilter.Select(candidates, requireOnline: false) : candidates;
            var visible = active.Select(d => d with
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
    private void Text(DrawingContext d, string value, double x, double y, double size, Brush brush, double pixelsPerDip = 1, bool medium = false, double? width = null, bool fit = false)
    {
        var available = width ?? Math.Max(1, SurfaceWidth - 29 - x);
        Art.Text(d, value, x, y + size * (1 - settings.TextScale) * .5, size * settings.TextScale, brush,
            pixelsPerDip, medium, available, settings.TextWeight, fit: fit || value.All(c => !char.IsLetter(c)));
    }
    protected override void OnRender(DrawingContext d)
    {
        base.OnRender(d);
        foreground = Art.Brush(Palette.Text); secondary = Art.Brush(Palette.Secondary);
        quiet = Art.Brush(Palette.Quiet); green = Art.Brush(Palette.Accent); line = Art.Brush(Palette.Line);
        var target = TargetLayout;
        var layout = TransitionLayout ?? target;
        double height = layout.Height + FooterHeight * FooterReveal, radius = settings.CornerRadius;
        double width = layout.Width;
        double inset = (width - SurfaceWidth) / 2;
        vocabulary.Update(settings, DateTimeOffset.Now, target.VocabularyRowLimit, target.VocabularyColumns);
        bool columnsChanged = drawnVocabularyColumns != target.VocabularyColumns;
        drawnVocabularyColumns = target.VocabularyColumns;
        SyncVocabularyOffset(snap: columnsChanged || vocabulary.Offset < vocabularyScrollTarget);
        var baseColor = Art.Brush(Palette.Base).Color;
        baseColor.A = PreviewMaterial ? (byte)212 : (byte)(settings.GlassOpacity * 255);
        var glass = new LinearGradientBrush(baseColor, Color.FromArgb((byte)Math.Max(16, baseColor.A - 18), baseColor.R, baseColor.G, baseColor.B), 95);
        d.PushClip(new RectangleGeometry(new(0, 0, width, height), radius, radius));
        d.DrawRoundedRectangle(glass, null, new(0, 0, width, height), radius, radius);
        var glow = new RadialGradientBrush(Color.FromArgb(Dark ? (byte)18 : (byte)68, 255, 255, 255), Colors.Transparent)
        { GradientOrigin = new(.1, .0), Center = new(.1, .0), RadiusX = 1.1, RadiusY = .7 };
        d.DrawRectangle(glow, null, new(0, 0, width, height));
        d.DrawRoundedRectangle(null, Art.Pen(Art.Brush(Dark ? "#38FFFFFF" : "#C8FFFFFF"), 1.1), new(.7, .7, width - 1.4, height - 1.4), Math.Max(0, radius - .7), Math.Max(0, radius - .7));
        for (int i = 0; i < layout.Sections.Length; i++)
        {
            var section = layout.Sections[i];
            if (section.Opacity <= 0 || section.Height <= 0) continue;
            bool transitioning = TransitionLayout is not null;
            if (transitioning)
            {
                d.PushOpacity(section.Opacity);
                d.PushClip(new RectangleGeometry(new Rect(0, section.Top - 15, width, section.Height + 15)));
            }
            bool isVocabulary = section.Kind == WidgetSection.Vocabulary;
            d.PushTransform(new TranslateTransform(isVocabulary ? 0 : inset, 0));
            if (i > 0 && !(section.Kind == WidgetSection.Todo && !settings.Todos.Any(t => !settings.HideCompletedTodos || !t.Completed)))
                Art.Line(d, Art.Pen(line, .8), 29, section.Top - 8, (isVocabulary ? width : SurfaceWidth) - 29, section.Top - 8);
            switch (section.Kind)
            {
                case WidgetSection.Clock:
                    Art.Text(d, DateTime.Now.ToString("HH:mm"), 29, section.Top - 9, 58, foreground, Dpi, width: 298, weight: settings.TextWeight, alignment: TextAlignment.Center);
                    Art.Text(d, DateTime.Now.ToString("MM.dd  dddd"), 29, section.Top + 65, 12, secondary, Dpi, width: 298, alignment: TextAlignment.Center);
                    break;
                case WidgetSection.Weather: DrawWeatherTransition(d, section); break;
                case WidgetSection.Devices: DrawDevices(d, section.Top); break;
                case WidgetSection.Metrics: DrawMetrics(d, section.Top); break;
                case WidgetSection.GpuMemory: DrawGpuMemory(d, section.Top); break;
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
                    var credit = state.Poetry is { } source ? $"— {source.DynastyMark} · {source.Author} · 《{source.Origin}》" : "—";
                    Art.Text(d, credit, 31, section.Top + 38, 11 * settings.TextScale, secondary, Dpi, width: 294,
                        weight: settings.TextWeight, alignment: TextAlignment.Right);
                    break;
                case WidgetSection.Vocabulary:
                    DrawVocabulary(d, section, width, target.VocabularyColumns);
                    break;
                case WidgetSection.Todo:
                    break;
            }
            d.Pop();
            if (transitioning) { d.Pop(); d.Pop(); }
        }
        if ((settings.ShowTodo || settings.ShowVocabulary) && FooterReveal > 0) {
            d.PushOpacity(FooterReveal); Art.Line(d, Art.Pen(line, .7), 31 + inset, layout.Height - 12, 325 + inset, layout.Height - 12); d.Pop();
        }
        d.Pop();
    }
    private static double VocabularyColumnWidth(double width, int columns) =>
        (width - 2 * WidgetLayout.VocabularyInset - (columns - 1) * WidgetLayout.VocabularyColumnGap) / columns;

    private void DrawVocabulary(DrawingContext drawing, SectionLayout section, double width, int columns)
    {
        double rowHeight = WidgetLayout.VocabularyRowHeight(settings);
        double offset = VocabularyOffset;
        int first = (int)Math.Floor(offset);
        int end = Math.Min(vocabulary.Order.Count, (int)Math.Ceiling(offset + section.Height / rowHeight) * columns);
        double columnWidth = VocabularyColumnWidth(width, columns);
        double stride = columnWidth + WidgetLayout.VocabularyColumnGap;
        drawing.PushClip(new RectangleGeometry(new Rect(0, section.Top, width, section.Height)));
        for (int i = first * columns; i < end; i++)
        {
            var entry = vocabulary.Order[i];
            double x = WidgetLayout.VocabularyInset + i % columns * stride;
            double y = section.Top + (i / columns - offset) * rowHeight;
            Text(drawing, entry.Word, x, y + 3, 22, foreground, Dpi, medium: true, width: columnWidth);
            if (!settings.VocabularyHideMeaning)
                Art.Text(drawing, entry.Meaning, x, y + WidgetLayout.VocabularyMeaningTop, settings.VocabularyMeaningFontSize * settings.TextScale,
                    secondary, Dpi, width: columnWidth, weight: settings.TextWeight);
        }
        drawing.Pop();
    }

    private void SyncVocabularyOffset(bool snap = false)
    {
        double target = vocabulary.Offset;
        if (!ReferenceEquals(drawnVocabularyOrder, vocabulary.Order))
        {
            drawnVocabularyOrder = vocabulary.Order;
            snap = true;
        }
        if (!snap && target == vocabularyScrollTarget) return;

        double current = VocabularyOffset;
        vocabularyScrollTarget = target;
        int generation = ++vocabularyScrollGeneration;
        void Finish()
        {
            if (generation != vocabularyScrollGeneration) return;
            SetValue(VocabularyOffsetProperty, target);
            BeginAnimation(VocabularyOffsetProperty, null);
        }
        if (snap || Math.Abs(current - target) < .001)
        {
            Finish();
            return;
        }
        var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        animation.Completed += (_, _) => Finish();
        BeginAnimation(VocabularyOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ScrollVocabulary(int delta)
    {
        var target = TargetLayout;
        vocabulary.Scroll(settings, delta, VocabularyOffset, DateTimeOffset.Now, target.VocabularyRowLimit, target.VocabularyColumns);
        if (vocabulary.Offset != vocabularyScrollTarget) HoverHints.Close(this);
        SyncVocabularyOffset();
    }
    private void DrawGpuMemory(DrawingContext d, double top)
    {
        var memory = state.GpuMemory;
        int columns = (settings.ShowGpuMemory ? 2 : 0) + (settings.ShowVramTemperature ? 1 : 0);
        if (columns == 0) return;
        double column = 300d / columns;
        void DrawColumn(int index, string title, string value)
        {
            double x = 30 + index * column;
            Text(d, title, x, top, 11, secondary, Dpi, width: column - 10);
            Text(d, value, x, top + 21, 16, foreground, Dpi, width: column - 10, fit: true);
        }
        if (settings.ShowGpuMemory)
        {
            DrawColumn(0, "专用显存", MemoryDisplay.Format(memory?.DedicatedBytes, memory?.DedicatedCapacityBytes, settings.VramDisplayMode));
            DrawColumn(1, "共享内存", MemoryDisplay.Format(memory?.SharedBytes, memory?.SharedCapacityBytes, settings.VramDisplayMode));
        }
        if (settings.ShowVramTemperature)
            DrawColumn(settings.ShowGpuMemory ? 2 : 0, "显存温度", memory?.VramCelsius is { } temperature ? $"{temperature:0.#} °C" : "—");
    }
    public HardwareMetric GpuMetricAt(double x) => !settings.ShowGpuMemory || settings.ShowVramTemperature && x >= 230
        ? HardwareMetric.VramTemperature
        : x >= (settings.ShowVramTemperature ? 130 : 180) ? HardwareMetric.GpuShared : HardwareMetric.GpuDedicated;
    private void DrawNetwork(DrawingContext d, double y)
    {
        var n = state.Network;
        Text(d, "网络", 31, y, 12, secondary, Dpi, medium: true);
        d.DrawEllipse(n?.Connected == true ? green : quiet, null, new(320, y + 9), 3, 3);
        Text(d, "↓ " + Speed(n?.Download), 31, y + 24, 13, foreground, Dpi, width: 146);
        Text(d, "↑ " + Speed(n?.Upload), 195, y + 24, 13, foreground, Dpi, width: 132);
        var targets = settings.NetworkProbeTargets?.ToArray() ?? new[] { settings.NetworkProbeTarget };
        for (int i = 0; i < targets.Length; i++) {
            var row = y + 56 + i * (settings.ShowNetworkProbeIp ? 52 : 32);
            var probe = n?.Targets.FirstOrDefault(p => p.Target == targets[i]);
            if (probe is null && n?.Target == targets[i] && n.Targets.Length == 0) probe = new(n.Target,n.Loss,n.Latency,n.Probes);
            if (settings.ShowNetworkProbeIp) { Text(d, targets[i], 31, row, 10, quiet, Dpi, width: 294); row += 17; }
            Text(d, "延迟", 31, row + 3, 10, secondary, Dpi, width: 30);
            Text(d, probe?.Latency is { } ms ? $"{ms} ms" : "— ms", 67, row, 14, foreground, Dpi, width: 112);
            Text(d, "丢包", 195, row + 3, 10, secondary, Dpi, width: 30);
            Text(d, probe?.Loss is { } loss ? $"{loss:0.#}%" : "—%", 231, row, 14, foreground, Dpi, width: 96);
        }
    }
    private static string Speed(double? value) => value is not { } v ? "—" : v >= 1048576 ? $"{v / 1048576:0.0} MiB/s" : $"{v / 1024:0.0} KiB/s";
    private void DrawWeatherTransition(DrawingContext d, SectionLayout section)
    {
        bool configured = settings.ShowWeather && (settings.WeatherCurrent || settings.WeatherTodayRange || settings.WeatherHourly || settings.WeatherDaily);
        if (!configured)
        {
            if (weatherFrame is null) return;
            d.PushTransform(new TranslateTransform(0, section.Top));
            d.DrawDrawing(weatherFrame);
            d.Pop();
            return;
        }
        var key = $"{settings.WeatherCurrent}|{settings.WeatherTodayRange}|{settings.WeatherHourly}|{settings.WeatherDaily}|{settings.ForecastHours}|{settings.ForecastDays}|{settings.HourStep}|{settings.HourlyIncludeCurrent}|{settings.CompactMode}";
        if (key != weatherConfiguration)
        {
            weatherConfiguration = key;
            int generation = ++weatherGeneration;
            weatherFrom = settings.SmoothResize ? weatherFrame : null;
            if (weatherFrom is not null) {
                var motion = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
                motion.Completed += (_, _) => { if (generation != weatherGeneration) return; SetValue(WeatherProgressProperty, 1.0); BeginAnimation(WeatherProgressProperty, null); weatherFrom = null; };
                BeginAnimation(WeatherProgressProperty, motion, System.Windows.Media.Animation.HandoffBehavior.SnapshotAndReplace);
            }
            else { SetValue(WeatherProgressProperty, 1.0); BeginAnimation(WeatherProgressProperty, null); }
        }
        var frame = new DrawingGroup();
        using (var context = frame.Open()) {
            double progress = weatherFrom is null ? 1 : (double)GetValue(WeatherProgressProperty);
            if (weatherFrom is not null) { context.PushOpacity(1 - progress); context.DrawDrawing(weatherFrom); context.Pop(); }
            context.PushOpacity(progress); DrawWeather(context, 0); context.Pop();
        }
        weatherFrame = frame;
        d.PushClip(new RectangleGeometry(new Rect(0, section.Top - 20, 356, section.Height + 20)));
        d.PushTransform(new TranslateTransform(0, section.Top)); d.DrawDrawing(frame); d.Pop(); d.Pop();
    }
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
        var area = Layout.Sections.First(s => s.Kind == WidgetSection.Devices);
        d.PushClip(new RectangleGeometry(new Rect(0, top, 356, area.Height)));
        var devices = VisibleDevices; var pages = Math.Max(1, (int)Math.Ceiling(devices.Count / 2.0));
        state.DevicePage = Math.Clamp(state.DevicePage, 0, pages - 1);
        var current = devices.Skip(state.DevicePage * 2).Take(2).ToArray();
        for (int row = 0; row < current.Length; row++)
        {
            double y = top + row * (settings.CompactMode ? 56 : 78);
            var peripheral = row < current.Length ? current[row] : null;
            var online = peripheral is not null && !(peripheral.DisplayDimmed ?? peripheral.State != LinkState.Online); var deviceLine = online ? secondary : quiet;
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
            bool wired = peripheral?.Link == LinkKind.Wired;
            Text(d, wired ? "有线连接" : peripheral?.Percent is { } percent ? $"{percent}%" : "—", textX, y + (wired ? 25 : 18), wired ? 13 : settings.CompactMode ? 18 : 26, online ? foreground : quiet, Dpi);
            if (!wired && peripheral?.Charging == true) Art.Icon(d, "bolt", textX + 75, y + 29, 12, green);
            var connection = peripheral?.Link switch { LinkKind.Bluetooth => "bluetooth", LinkKind.Receiver => "receiver", LinkKind.NearLink => "nearlink", LinkKind.Wired => "wired", _ => "" };
            if (settings.ShowConnectionIcons && connection.Length > 0) Art.Icon(d, connection, 288, y + 25, 17, deviceLine, 1.05);
            d.DrawEllipse(online ? green : line, null, new(322, y + 34), 2.8, 2.8);
            if (!settings.CompactMode && !wired)
            {
                d.DrawRoundedRectangle(line, null, new(textX, y + 59, 204, 2.5), 1.25, 1.25);
                if (peripheral?.Percent is { } pct) d.DrawRoundedRectangle(online ? pct <= settings.BatteryThreshold ? Art.Brush("#B39B70") : green : quiet, null, new(textX, y + 59, 204.0 * pct / 100, 2.5), 1.25, 1.25);
            }
        }
        if (pages > 1) for (var i = 0; i < Math.Min(pages, 10); i++)
            d.DrawEllipse(i == state.DevicePage ? secondary : line, null, new(178 + (i - (Math.Min(pages, 10) - 1) / 2.0) * 8, top + (settings.CompactMode ? 110 : 151)), 1.6, 1.6);
        d.Pop();
    }
    private (string Icon, double? Value, string Tip)[] Metrics => new[] {
        ("cpu", state.System.CpuPercent, "CPU 总负载 · 最近 80 秒", settings.ShowCpu),
        ("memory", state.System.MemoryPercent, "物理内存占用 · 最近 80 秒\n" + MemoryDisplay.Format(state.System.MemoryUsedBytes, state.System.MemoryTotalBytes, MemoryDisplayMode.Capacity), settings.ShowMemory),
        ("temperature", state.System.CpuCelsius, state.System.TemperatureStatus, settings.ShowTemperature)
    }.Where(v => v.Item4).Select(v => (v.Item1, v.Item2, v.Item3)).ToArray();
    private void DrawMetrics(DrawingContext d, double top)
    {
        var metrics = Metrics;
        for (int i = 0; i < metrics.Length; i++)
        {
            double column = 300.0 / metrics.Length, x = 30 + i * column;
            var m = metrics[i]; Art.Icon(d, m.Icon, x, top + 13, 17, secondary, 1.05);
            bool capacity = m.Icon == "memory" && settings.RamDisplayMode == MemoryDisplayMode.Capacity;
            string value = capacity ? MemoryDisplay.Format(state.System.MemoryUsedBytes, state.System.MemoryTotalBytes, settings.RamDisplayMode) :
                m.Value is { } v ? $"{v:0}" + (m.Icon == "temperature" ? "°" : "%") : "—";
            Text(d, value, x + 25, top + (capacity ? 9 : 3), capacity ? 15 : 24, foreground, Dpi, width: column - 25, fit: capacity);
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
        if (settings.PanelLocked) { e.Handled = true; return; }
        var words = Layout.Sections.FirstOrDefault(s => s.Kind == WidgetSection.Vocabulary);
        var mouse = e.GetPosition(this);
        if (words is not null && mouse.Y >= words.Top && mouse.Y < words.Top + words.Height)
        {
            ScrollVocabulary(e.Delta);
            e.Handled = true; return;
        }
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
                var stateText = device.DisplayDimmed == true ? "15 分钟无新信号 · 上次读数" : device.State == LinkState.Online ? "已连接" : "休眠宽限 · 上次读数";
                tip = $"{device.Name}\n{connection} · {stateText}";
            }
            else tip = "打开设置查看外设检测";
            if (devices.Count > 2) tip += "\n滚轮切换设备";
        }
        if (area?.Kind == WidgetSection.Metrics && Metrics.Length > 0) tip = Metrics[Math.Clamp((int)((p.X - SectionInset - 30) / (300.0 / Metrics.Length)), 0, Metrics.Length - 1)].Tip + (settings.PanelLocked ? "" : "\n单击查看历史曲线");
        if (area?.Kind == WidgetSection.GpuMemory)
        {
            var gpu = state.GpuMemory;
            var metric = GpuMetricAt(p.X - SectionInset);
            if (metric == HardwareMetric.VramTemperature)
                tip = gpu?.VramCelsius is { } temp ? $"显存温度 {temp:0.#} °C\n{gpu.VramTemperatureSource}" : "显卡或驱动暂未提供显存温度读数";
            else
            {
                bool shared = metric == HardwareMetric.GpuShared;
                var used = shared ? gpu?.SharedBytes : gpu?.DedicatedBytes;
                var total = shared ? gpu?.SharedCapacityBytes : gpu?.DedicatedCapacityBytes;
                tip = $"{HardwareMetricInfo.Title(metric)} {MemoryDisplay.Format(used, total, MemoryDisplayMode.Capacity)}";
                if (total is null && used is not null) tip += "\n总量不可用";
                if (!string.IsNullOrEmpty(gpu?.Source)) tip += "\n" + gpu.Source;
            }
            if (!settings.PanelLocked) tip += "\n单击查看历史曲线";
        }
        Cursor = !settings.PanelLocked && (area?.Kind is WidgetSection.Metrics or WidgetSection.GpuMemory) ? Cursors.Hand : Cursors.Arrow;
        if (area?.Kind == WidgetSection.Network) tip = NetworkHint;
        if (area?.Kind == WidgetSection.Vocabulary) {
            var row = (int)Math.Floor(VocabularyOffset + (p.Y - area.Top) / WidgetLayout.VocabularyRowHeight(settings));
            var columnWidth = VocabularyColumnWidth(Width, drawnVocabularyColumns);
            var stride = columnWidth + WidgetLayout.VocabularyColumnGap;
            var x = p.X - WidgetLayout.VocabularyInset;
            var column = (int)Math.Floor(x / stride);
            var index = row * drawnVocabularyColumns + column;
            if (TransitionLayout is null && Math.Abs(VocabularyOffset - vocabularyScrollTarget) < .01 &&
                row >= 0 && column >= 0 && column < drawnVocabularyColumns && x - column * stride < columnWidth &&
                index < vocabulary.Order.Count) {
                var word = vocabulary.Order[index];
                tip = word.Word + (settings.VocabularyHideMeaning ? "" : "\n" + word.Meaning) + "\n滚轮滚动单词";
            }
        }
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
