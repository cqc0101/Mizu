using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Mizu.Core.Services;
using Mizu.Widget.Controls;

namespace Mizu.Widget;

public sealed class HardwareHistoryWindow : Window
{
    private readonly MainWindow widget;
    private readonly SettingsTheme theme;
    private readonly HardwareHistoryChart chart = new() { MinHeight = 190 };
    private readonly TextBlock status = new(), chartTitle = new(), statistics = new(), note = new();
    private readonly Dictionary<HardwareMetric, (Button Button, TextBlock Value, TextBlock State)> cards = new();
    private readonly Dictionary<int, Button> ranges = new();
    private HardwareMetric selected = HardwareMetric.Cpu;
    private int minutes = 15;
    private string? lastTheme;

    public HardwareHistoryWindow(MainWindow widget, HardwareMetric? initialMetric = null)
    {
        this.widget = widget;
        var work = SystemParameters.WorkArea;
        double availableWidth = Math.Max(160, work.Width - 24), availableHeight = Math.Max(160, work.Height - 24);
        Title = "Mizu · 硬件历史"; Icon = AppIcons.WindowIcon;
        Width = Math.Min(860, availableWidth); Height = Math.Min(630, availableHeight);
        MinWidth = Math.Min(680, availableWidth); MinHeight = Math.Min(560, availableHeight);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI"); FontSize = 12;
        UseLayoutRounding = true;
        theme = new(this);
        Background = theme.Brush("#F0F1EC"); Foreground = theme.Brush("#41483E");
        Resources.Add(typeof(Button), ButtonStyle());
        var root = new Grid { Margin = new(28, 22, 28, 20), MinWidth = 624, MinHeight = 460 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var viewport = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var host = new Grid { Background = Background }; host.Children.Add(viewport); Content = host;

        var heading = new Grid { Margin = new(0, 0, 0, 22) };
        heading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(new TextBlock { Text = "硬件历史", FontSize = 25, FontWeight = FontWeights.Medium });
        status.Foreground = theme.Brush("#8D9487"); status.FontSize = 11; status.Margin = new(1, 7, 0, 0);
        titles.Children.Add(status); heading.Children.Add(titles);
        var rangePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (int range in new[] { 5, 15, 60 }) {
            var button = new Button { Content = range == 60 ? "1 小时" : range + " 分钟", Padding = new(13, 9, 13, 9), Margin = new(3, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(button, "查看最近" + button.Content);
            button.Click += (_, _) => { minutes = range; Refresh(); FadeChart(); };
            ranges.Add(range, button); rangePanel.Children.Add(button);
        }
        var rangeBorder = new Border { Child = rangePanel, Padding = new(3), CornerRadius = new(13), Background = theme.Brush("#E7EBE1") };
        Grid.SetColumn(rangeBorder, 1); heading.Children.Add(rangeBorder); root.Children.Add(heading);

        var metrics = new UniformGrid { Rows = 1, Margin = new(-4, 0, -4, 20) };
        foreach (var metric in Enum.GetValues<HardwareMetric>()) {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = HardwareMetricInfo.Title(metric), FontSize = 11, Foreground = theme.Brush("#8D9487") });
            var value = new TextBlock { FontSize = 26, FontWeight = FontWeights.Light, Margin = new(0, 9, 0, 7) };
            body.Children.Add(value);
            var state = new TextBlock { FontSize = 10, Foreground = theme.Brush("#8D9487") };
            body.Children.Add(state);
            var button = new Button { Content = body, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(13, 13, 9, 12), Margin = new(4, 0, 4, 0) };
            System.Windows.Automation.AutomationProperties.SetName(button, HardwareMetricInfo.Title(metric) + "历史");
            button.Click += (_, _) => { selected = metric; Refresh(); FadeChart(); };
            cards.Add(metric, (button, value, state)); metrics.Children.Add(button);
        }
        Grid.SetRow(metrics, 1); root.Children.Add(metrics);

        var chartPanel = new Grid { Margin = new(17, 17, 17, 7) };
        chartPanel.RowDefinitions.Add(new() { Height = GridLength.Auto });
        chartPanel.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var chartHeading = new Grid { Margin = new(3, 0, 3, 12) };
        chartHeading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        chartHeading.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        chartTitle.FontSize = 13; chartTitle.FontWeight = FontWeights.Medium;
        statistics.FontSize = 11; statistics.Foreground = theme.Brush("#8D9487");
        statistics.HorizontalAlignment = HorizontalAlignment.Right; statistics.VerticalAlignment = VerticalAlignment.Center;
        HoverHints.Set(statistics, "按当前时间范围内的有效采样统计，缺失读数不计入。");
        chartHeading.Children.Add(chartTitle); Grid.SetColumn(statistics, 1); chartHeading.Children.Add(statistics);
        chartPanel.Children.Add(chartHeading); Grid.SetRow(chart, 1); chartPanel.Children.Add(chart);
        var frame = new Border { CornerRadius = new(18), Background = theme.Brush("#FCFCF8"), BorderBrush = theme.Brush("#E7EBE1"), BorderThickness = new(1), Child = chartPanel };
        Grid.SetRow(frame, 2); root.Children.Add(frame);

        note.Margin = new(2, 13, 2, 0); note.FontSize = 11; note.TextWrapping = TextWrapping.Wrap; note.Foreground = theme.Brush("#8D9487");
        Grid.SetRow(note, 3); root.Children.Add(note);
        widget.State.HardwareHistory.Changed += Refresh;
        widget.SettingsApplied += Refresh;
        SourceInitialized += (_, _) => Refresh();
        Closed += (_, _) => { widget.State.HardwareHistory.Changed -= Refresh; widget.SettingsApplied -= Refresh; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        if (initialMetric is { } metricChoice) selected = metricChoice;
        Refresh();
    }
    public void SelectMetric(HardwareMetric metric) { selected = metric; Refresh(); FadeChart(); }
    private Style ButtonStyle()
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.ForegroundProperty, theme.Brush("#41483E")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(CursorProperty, Cursors.Hand));
        var border = new FrameworkElementFactory(typeof(Border)); border.Name = "Body";
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        presenter.SetBinding(HorizontalAlignmentProperty, new System.Windows.Data.Binding("HorizontalContentAlignment") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(OpacityProperty, .78)); template.Triggers.Add(hover);
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true }; pressed.Setters.Add(new Setter(OpacityProperty, .55)); template.Triggers.Add(pressed);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Control.BorderBrushProperty, theme.Brush("#8B9F7F"))); template.Triggers.Add(focus);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
    private void FadeChart() => chart.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(160)));
    private void Refresh()
    {
        var settings = widget.Settings; var now = DateTimeOffset.UtcNow;
        theme.Apply(settings.Theme);
        bool recording = settings.RecordHardwareHistory && HardwareHistory.MonitoringEnabled(settings);
        status.Text = recording ? "正在记录  ·  本次运行最近 1 小时" : "记录已暂停  ·  本次运行的历史仍可查看";
        var histories = cards.Keys.ToDictionary(metric => metric, metric => widget.State.HardwareHistory.Read(metric, now, HardwareHistory.Retention));
        var available = cards.Keys.Where(metric => HardwareMetricInfo.Enabled(metric, settings) || histories[metric].Any(s => s.Value.HasValue)).ToArray();
        if (!available.Contains(selected) && available.Length > 0) selected = available[0];
        foreach (var entry in cards) {
            var metric = entry.Key; var card = entry.Value;
            var history = histories[metric];
            bool enabled = HardwareMetricInfo.Enabled(metric, settings);
            card.Button.Visibility = enabled || history.Any(s => s.Value.HasValue) ? Visibility.Visible : Visibility.Collapsed;
            var last = history.LastOrDefault();
            double? value = recording && enabled && now - last.Time <= TimeSpan.FromSeconds(12) ? last.Value : null;
            var formatted = HardwareMetricInfo.Format(metric, value);
            card.Value.Inlines.Clear();
            card.Value.Inlines.Add(new Run(formatted.Value));
            card.Value.Inlines.Add(new Run(" " + formatted.Unit) { FontSize = 11, Foreground = theme.Brush("#8D9487") });
            card.State.Text = !recording || !enabled ? "已暂停" : value is null ? "暂无读数" : "当前";
            card.Button.Background = theme.Brush(metric == selected ? "#DEE6D7" : "#FCFCF8");
            card.Button.BorderBrush = metric == selected ? theme.Brush("#8B9F7F") : Brushes.Transparent;
        }
        foreach (var entry in ranges) {
            entry.Value.Background = entry.Key == minutes ? theme.Brush("#FCFCF8") : Brushes.Transparent;
            entry.Value.BorderBrush = Brushes.Transparent;
            entry.Value.FontWeight = entry.Key == minutes ? FontWeights.Medium : FontWeights.Normal;
        }
        var samples = widget.State.HardwareHistory.Read(selected, now, TimeSpan.FromMinutes(minutes));
        var values = samples.Where(s => s.Value.HasValue).Select(s => s.Value!.Value).ToArray();
        chartTitle.Text = HardwareMetricInfo.Title(selected);
        string Format(double v) { var f = HardwareMetricInfo.Format(selected, v); return f.Value + " " + f.Unit; }
        statistics.Text = values.Length == 0 ? "" : "最低 " + Format(values.Min()) + "    平均 " + Format(values.Average()) + "    峰值 " + Format(values.Max());
        chart.Update(selected, samples, now, TimeSpan.FromMinutes(minutes), ThemePalette.Get(settings.Theme));
        note.Text = selected == HardwareMetric.CpuTemperature && !settings.TemperatureEnabled
            ? "温度读取尚未开启，可在设置中启用；无法读取时保留空缺。"
            : selected == HardwareMetric.VramTemperature && values.Length == 0
                ? "显存温度需要显卡和驱动提供读数；暂不可用时保留空缺。"
            : selected == HardwareMetric.GpuShared && values.Length == 0
                ? "共享 GPU 内存需要驱动提供读数；当前不可用时保留空缺。"
                : "悬停曲线查看时间与读数  ·  历史仅保存在内存中，退出后清空。";
        if (lastTheme is not null && lastTheme != settings.Theme) FadeChart();
        lastTheme = settings.Theme;
    }
}
