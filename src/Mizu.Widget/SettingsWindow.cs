using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Mizu.Core;
using Mizu.Core.Services;
using Mizu.Widget.Controls;

namespace Mizu.Widget;

public sealed class SettingsWindow : Window
{
    private readonly MainWindow widget;
    private readonly SettingsTheme theme;
    private readonly AppSettings settings;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 12, 0, 0) };
    private readonly StackPanel probeList = new();
    private readonly StackPanel deviceList = new();
    private readonly StackPanel todoList = new();
    private readonly List<CheckBox> todoVisibilityToggles = [];
    private readonly List<CheckBox> poetryVisibilityToggles = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly System.Windows.Threading.DispatcherTimer saveDelay = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private bool adjustingSlider, pendingSave;
    public SettingsWindow(MainWindow widget)
    {
        this.widget = widget; settings = widget.Settings;
        theme = new(this);
        theme.Brush("#40573D");
        theme.Brush("#4B5843");
        theme.Brush("#5C7650");
        theme.Brush("#7B8674");
        theme.Brush("#8A9F7B");
        theme.Brush("#8B9980");
        theme.Brush("#8B9F7F");
        theme.Brush("#8EA37F");
        theme.Brush("#90A37E");
        theme.Brush("#9AAC8C");
        theme.Brush("#D7DFCE");
        theme.Brush("#D9E2CF");
        theme.Brush("#DAE1D2");
        theme.Brush("#DDE2D6");
        theme.Brush("#DEE4D6");
        theme.Brush("#DEE6D7");
        theme.Brush("#DFE5D7");
        theme.Brush("#E1E8D8");
        theme.Brush("#E7EBE1");
        theme.Brush("#E8EBE1");
        theme.Brush("#E8EDDF");
        theme.Brush("#F0F3EA");
        theme.Brush("#F1F4EB");
        theme.Brush("#FCFCF8");
        theme.Brush("#FCFDF8");
        theme.Apply(settings.Theme); status.Foreground = theme.Brush("#8B7758");
        Title = "Mizu · 设置"; Width = 780; Height = 790; MinWidth = 680; MinHeight = 560;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = theme.Brush("#F0F1EC"); Foreground = theme.Brush("#41483E");
        FontFamily = new("Segoe UI, Microsoft YaHei UI"); FontSize = 13;
        var root = new Grid { Margin = new(24, 22, 24, 20), Background = Background };
        root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Mizu;component/SettingsStyles.xaml", UriKind.Relative) });
        root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, Foreground);
        root.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, FontFamily);
        root.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, FontSize);
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new(0, 0, 0, 18) };
        heading.Children.Add(new TextBlock { Text = "Mizu", FontSize = 28, FontWeight = FontWeights.Light });
        heading.Children.Add(new TextBlock { Text = "偏好设置", Margin = new(1, 5, 0, 0), Foreground = theme.Brush("#8D9487"), FontSize = 12 }); root.Children.Add(heading);
        var tabs = new TabControl { Background = Brushes.Transparent, BorderThickness = new(0), TabStripPlacement = Dock.Left };
        AddTab(tabs, "外观", Appearance()); AddTab(tabs, "显示内容", Contents()); AddTab(tabs, "天气", Weather()); AddTab(tabs, "诗词", Poetry()); AddTab(tabs, "待办", Todos()); AddTab(tabs, "设备", Devices()); AddTab(tabs, "推送", Push());
        Grid.SetRow(tabs, 1); root.Children.Add(tabs); Grid.SetRow(status, 2); root.Children.Add(status); Content = root;
        saveDelay.Tick += (_, _) => FlushPendingSave();
        Closed += (_, _) => { FlushPendingSave(); lifetime.Cancel(); lifetime.Dispose(); };
        SourceInitialized += (_, _) => theme.Apply(settings.Theme);
        BuildDevices(); BuildTodoList();
        Activated += (_, _) => { BuildDevices(); BuildTodoList(); };
    }
    private void AddTab(TabControl tabs, string title, UIElement content) => tabs.Items.Add(new TabItem
    { Header = new TextBlock { Text = title, Margin = new(16, 13, 16, 13) }, Content = new Border { Background = theme.Brush("#FCFCF8"), CornerRadius = new(18), Padding = new(24, 20, 15, 16), Child = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } } });
    private TextBlock Label(string text, bool detail = false) => new() { Text = text, FontSize = detail ? 12 : 13, TextWrapping = TextWrapping.Wrap,
        Foreground = theme.Brush(detail ? "#8A9183" : "#434B3E"), Margin = new(0, 0, 0, detail ? 15 : 9), LineHeight = detail ? 20 : double.NaN };
    private Button ActionButton(string text, RoutedEventHandler action)
    {
        var button = new Button { Content = text, Padding = new(15, 8, 15, 8), Margin = new(0, 6, 8, 6), Background = theme.Brush("#E5E8DC"), BorderBrush = theme.Brush("#D5DACE"), Foreground = theme.Brush("#4A5945"), Cursor = Cursors.Hand };
        button.Click += action; return button;
    }
    private CheckBox Toggle(string title, bool value, Action<bool> apply)
    {
        var check = new CheckBox { Content = title, IsChecked = value, Margin = new(0, 9, 0, 9), VerticalContentAlignment = VerticalAlignment.Center };
        void Set(bool enabled)
        {
            apply(enabled);
            if (title is "待办清单" or "在组件上显示待办")
                foreach (var other in todoVisibilityToggles.Where(t => t != check && t.IsChecked != enabled)) other.IsChecked = enabled;
            if (title is "每日诗词" or "开启每日诗词")
                foreach (var other in poetryVisibilityToggles.Where(t => t != check && t.IsChecked != enabled)) other.IsChecked = enabled;
        }
        if (title is "待办清单" or "在组件上显示待办") todoVisibilityToggles.Add(check);
        if (title is "每日诗词" or "开启每日诗词") poetryVisibilityToggles.Add(check);
        check.Checked += (_, _) => Set(true); check.Unchecked += (_, _) => Set(false); return check;
    }
    private UIElement SliderRow(string title, double value, double min, double max, double step, string format, Action<double> apply, bool continuous = false)
    {
        var grid = new Grid { Margin = new(0, 8, 0, 15) }; grid.ColumnDefinitions.Add(new() { Width = new(112) }); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new(58) });
        var label = Label(title); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new(0); grid.Children.Add(label);
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, TickFrequency = step, SmallChange = step, IsSnapToTickEnabled = !continuous, VerticalAlignment = VerticalAlignment.Center, Margin = new(4, 0, 12, 0), ToolTip = null };
        Grid.SetColumn(slider, 1); grid.Children.Add(slider);
        var number = new TextBlock { Text = value.ToString(format), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Foreground = theme.Brush("#87917C") };
        Grid.SetColumn(number, 2); grid.Children.Add(number);
        slider.ValueChanged += (_, e) =>
        {
            number.Text = e.NewValue.ToString(format); adjustingSlider = true;
            try { apply(e.NewValue); } finally { adjustingSlider = false; }
        }; return grid;
    }
    private void FlushPendingSave() { saveDelay.Stop(); if (pendingSave) { pendingSave = false; widget.SaveSettings(); } }
    private void Save(bool scaleOnly = false)
    {
        settings.Validate(); theme.Apply(settings.Theme);
        if (adjustingSlider)
        {
            widget.RequestSettingsPreview(scaleOnly); pendingSave = true; saveDelay.Stop(); saveDelay.Start();
        }
        else { widget.ApplySettings(); widget.SaveSettings(); }
    }
    private UIElement Appearance()
    {
        var panel = new StackPanel { Margin = new(1, 8, 12, 0) };
        panel.Children.Add(Label("半透明材质，暖灰与留白。", true));
        var themes = new ComboBox { ItemsSource = ThemePalette.All.Select(p => p.Name).ToArray(), SelectedIndex = Array.FindIndex(ThemePalette.All, p => p.Id == settings.Theme), Margin = new(0, 0, 0, 17), Padding = new(10, 7, 10, 7) };
        themes.SelectionChanged += (_, _) => { if (themes.SelectedIndex >= 0) { settings.Theme = ThemePalette.All[themes.SelectedIndex].Id; Save(); } }; panel.Children.Add(themes);
        panel.Children.Add(SliderRow("玻璃浓度", settings.GlassOpacity * 100, 15, 95, 5, "0'%'", v => { settings.GlassOpacity = v / 100; Save(); }));
        panel.Children.Add(SliderRow("圆角半径 R", settings.CornerRadius, 0, 64, 1, "0", v => { settings.CornerRadius = v; Save(); }));
        panel.Children.Add(SliderRow("组件大小", settings.Scale * 100, 75, 150, 1, "0'%'", v => { settings.Scale = v / 100; Save(scaleOnly: true); }, continuous: true));
        panel.Children.Add(Toggle("平滑缩放动画", settings.SmoothResize, v => { settings.SmoothResize = v; Save(); }));
        panel.Children.Add(SliderRow("组件字号", settings.TextScale * 100, 85, 125, 1, "0'%'", v => { settings.TextScale = v / 100; Save(); }, continuous: true));
        panel.Children.Add(Label("组件字重"));
        var weights = new[] { 300, 400, 500, 600, 700 };
        var weight = new ComboBox { ItemsSource = new[] { "纤细", "常规", "适中", "半粗", "粗体" }, SelectedIndex = Math.Max(0, Array.IndexOf(weights, settings.TextWeight)), Margin = new(0, 0, 0, 15) };
        weight.SelectionChanged += (_, _) => { if (weight.SelectedIndex >= 0) { settings.TextWeight = weights[weight.SelectedIndex]; Save(); } }; panel.Children.Add(weight);
        panel.Children.Add(Toggle("始终置顶", settings.AlwaysOnTop, v => { settings.AlwaysOnTop = v; Save(); }));
        panel.Children.Add(Toggle("开机启动", settings.AutoStart, v =>
        {
            try { NativeWindow.SetAutoStart(v); settings.AutoStart = v; Save(); }
            catch (Exception ex) { status.Text = "无法更新开机启动设置"; AppLog.Write("startup", ex.GetType().Name); }
        }));
        panel.Children.Add(Toggle("鼠标穿透", settings.ClickThrough, v => { settings.ClickThrough = v; Save(); }));
        panel.Children.Add(Label("拖动组件可移动。悬停显示按钮，双击打开设置。\nCtrl+Alt+M 恢复鼠标操作；托盘也可随时找回组件。", true));
        panel.Children.Add(Label("当前使用半透明渐变材质，暂不含实时背景模糊。", true));
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(ActionButton("重置位置", (_, _) => { var r = SystemParameters.WorkArea; widget.Left = r.Right - widget.Width - 40; widget.Top = r.Top + 50; settings.Left = widget.Left; settings.Top = widget.Top; Save(); }));
        actions.Children.Add(ActionButton("退出 Mizu", async (_, _) => await widget.QuitAsync())); panel.Children.Add(actions); return panel;
    }
    private UIElement Contents()
    {
        var panel = new StackPanel { Margin = new(1, 4, 12, 0) };
        panel.Children.Add(Label("留在桌面上的，由你决定。", true));
        panel.Children.Add(Toggle("天气", settings.ShowWeather, v => { settings.ShowWeather = v; Save(); }));
        panel.Children.Add(Toggle("外设电量", settings.ShowDevices, v => { settings.ShowDevices = v; Save(); }));
        panel.Children.Add(Toggle("CPU 负载", settings.ShowCpu, v => { settings.ShowCpu = v; Save(); }));
        panel.Children.Add(Toggle("内存占用", settings.ShowMemory, v => { settings.ShowMemory = v; Save(); }));
        panel.Children.Add(Toggle("CPU 温度", settings.ShowTemperature, v => { settings.ShowTemperature = v; Save(); }));
        panel.Children.Add(Toggle("网络状态与上下行", settings.ShowNetwork, v => { settings.ShowNetwork = v; Save(); }));
        panel.Children.Add(Label("显示当前活动网卡速率。开启后每 2 秒 ICMP 探测一次；丢包率为最近 60 次到目标的探测失败比例，不代表所有网络流量。", true));
        panel.Children.Add(Toggle("在组件显示探测 IP", settings.ShowNetworkProbeIp, v => { settings.ShowNetworkProbeIp = v; Save(); }));
        panel.Children.Add(Label("最多 8 个 IPv4 / IPv6，各自统计最近 60 次探测。关闭 IP 显示后，组件及悬停提示都只显示探测序号；上下行仍是网卡总速率。", true));
        panel.Children.Add(new ScrollViewer { Content = probeList, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var newProbe = new TextBox { MaxLength = 64, Padding = new(8), Background = theme.Brush("#FAFAF5"), BorderBrush = theme.Brush("#DBDFD3"), Margin = new(0,5,0,4) };
        panel.Children.Add(Label("添加探测 IP（例如网关、DNS 或服务器）", true));panel.Children.Add(newProbe);
        void AddProbe() {
            var address = NetworkService.NormalizeTarget(newProbe.Text);
            var targets = settings.NetworkProbeTargets ?? new List<string> { settings.NetworkProbeTarget };
            if (address is null) { status.Text = "请输入有效的 IPv4 或 IPv6 单播地址"; return; }
            if (targets.Contains(address,StringComparer.OrdinalIgnoreCase)) { status.Text = "此 IP 已在列表中"; return; }
            if (targets.Count >= 8) { status.Text = "最多 8 个探测 IP"; return; }
            settings.NetworkProbeTargets = targets.Append(address).ToList(); Save(); newProbe.Clear(); BuildProbeList();
        }
        newProbe.KeyDown += (_, e) => { if(e.Key == Key.Enter) { AddProbe();e.Handled=true; } };
        panel.Children.Add(ActionButton("添加 IP", (_, _) => AddProbe())); BuildProbeList();
        panel.Children.Add(Toggle("当前播放与媒体控制", settings.ShowMedia, v => { settings.ShowMedia = v; Save(); }));
        panel.Children.Add(Label("读取 Windows 当前媒体会话。播放器需要支持系统媒体控制；媒体功能要求 Windows 10 1809+。", true));
        panel.Children.Add(Toggle("每日诗词", settings.ShowPoetry, v => { settings.ShowPoetry = v; Save(); }));
        panel.Children.Add(Toggle("待办清单", settings.ShowTodo, v => { settings.ShowTodo = v; Save(); }));
        panel.Children.Add(new Separator { Margin = new(0, 16, 0, 16) });
        panel.Children.Add(Toggle("精简布局", settings.CompactMode, v => { settings.CompactMode = v; Save(); }));
        panel.Children.Add(Label("减小天气与外设行间距，隐藏电量条和负载曲线。关闭后恢复完整布局。", true));
        panel.Children.Add(Toggle("负载趋势曲线", settings.ShowGraphs, v => { settings.ShowGraphs = v; Save(); }));
        panel.Children.Add(Toggle("外设连接方式图标", settings.ShowConnectionIcons, v => { settings.ShowConnectionIcons = v; Save(); }));
        panel.Children.Add(Label("设置即时生效并自动保存。关闭显示只影响组件内容，已启用的阈值提醒继续工作。内容较多时整体缩小以适应屏幕高度。", true));
        return panel;
    }
    private void BuildProbeList()
    {
        probeList.Children.Clear();
        var targets = settings.NetworkProbeTargets ?? new List<string> { settings.NetworkProbeTarget };
        foreach (var address in targets) {
            var row = new Grid { Margin = new(0,3,0,3) }; row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new() { Width = new(42) });
            var input = new TextBox { Text = address, MaxLength = 64, Padding = new(8), Background = theme.Brush("#FAFAF5"), BorderBrush = theme.Brush("#DBDFD3") };
            input.LostFocus += (_, _) => {
                if(input.Text == address) return;
                var replacement = NetworkService.NormalizeTarget(input.Text);
                var current = settings.NetworkProbeTargets ?? targets;
                if(replacement is null || (replacement != address && current.Contains(replacement,StringComparer.OrdinalIgnoreCase))) { input.Text=address;status.Text="IP 无效或已存在";return; }
                settings.NetworkProbeTargets = current.Select(ip => ip == address ? replacement : ip).ToList();Save();BuildProbeList();
            };
            row.Children.Add(input);
            var remove = ActionButton("×", (_, _) => { settings.NetworkProbeTargets = (settings.NetworkProbeTargets ?? targets).Where(ip => ip != address).ToList();Save();BuildProbeList(); });
            remove.Padding=new(6);remove.Margin=new(5,0,0,0);HoverHints.Set(remove,"删除探测 IP");Grid.SetColumn(remove,1);row.Children.Add(remove);probeList.Children.Add(row);
        }
    }
    private UIElement Weather()
    {
        var panel = new StackPanel { Margin = new(1, 8, 12, 0) };
        panel.Children.Add(Label("选择城市"));
        panel.Children.Add(Label("天气来自 Open-Meteo，成功后每 20 分钟更新；首次失败或后续更新失败时每 30 秒重试。只发送所选城市坐标。", true));
        var query = new TextBox { Padding = new(10), FontSize = 15, Text = settings.Location?.Name ?? "", BorderBrush = theme.Brush("#D8DCCD"), Background = theme.Brush("#FAFAF6") };
        panel.Children.Add(query);
        var current = Label(settings.Location?.ToString() ?? "尚未选择城市", true); current.Margin = new(0, 15, 0, 10);
        var choices = new ListBox { MinHeight = 130, MaxHeight = 220, Visibility = Visibility.Collapsed, Background = Brushes.Transparent, BorderBrush = theme.Brush("#D8DCCD"), Margin = new(0, 6, 0, 15) };
        var searchVersion = 0;
        async Task Search()
        {
            var version = ++searchVersion;
            try
            {
                status.Text = "搜索中…"; var results = await widget.Engine.Weather.SearchAsync(query.Text, lifetime.Token);
                if (version != searchVersion) return;
                choices.ItemsSource = results; choices.Visibility = results.Length == 0 ? Visibility.Collapsed : Visibility.Visible; status.Text = results.Length == 0 ? "没有找到城市，可尝试拼音或英文名称" : "请选择匹配的城市";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = "暂时无法搜索，请检查网络"; AppLog.Write("weather.search", ex.GetType().Name); }
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(ActionButton("搜索", async (_, _) => await Search()));
        buttons.Children.Add(ActionButton("清除城市", async (_, _) => { settings.Location = null; Save(); current.Text = "尚未选择城市"; await widget.Engine.RefreshWeatherAsync(); }));
        panel.Children.Add(buttons); panel.Children.Add(current); panel.Children.Add(choices);
        query.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Search(); };
        panel.Children.Add(ActionButton("使用所选城市", async (_, _) =>
        {
            if (choices.SelectedItem is not Place place) { status.Text = "请先搜索并选择城市"; return; }
            settings.Location = place; widget.State.Weather = null; Save(); current.Text = place.ToString(); status.Text = "正在更新天气…";
            await widget.Engine.RefreshWeatherAsync(); status.Text = widget.State.WeatherFailed ? "天气暂不可用，稍后会自动重试" : "城市已保存";
        }));
        panel.Children.Add(new Separator { Margin = new(0, 16, 0, 16) });
        panel.Children.Add(Toggle("天气主源异常时自动切换备用", settings.WeatherBackupEnabled, v => { settings.WeatherBackupEnabled = v; Save(); }));
        panel.Children.Add(Label("主源 Open-Meteo，备用 wttr.in。备用预报约 3 天、每 3 小时一段，缺少的数据显示 —；两者失败时保留已有天气缓存。", true));
        panel.Children.Add(Label("天气缓存"));
        panel.Children.Add(Toggle("启动时读取本地天气缓存", settings.WeatherCacheEnabled, v => { settings.WeatherCacheEnabled = v; Save(); if (!v) widget.Engine.ClearWeatherCache(); }));
        panel.Children.Add(SliderRow("缓存有效期", settings.WeatherCacheHours, 1, 72, 1, "0' 小时'", v => { settings.WeatherCacheHours = (int)v; Save(); }));
        panel.Children.Add(Label("缓存按城市匹配，过期不读取；先显示缓存，再立即联网。悬停可查看原始更新时间。", true));
        panel.Children.Add(ActionButton("清除天气缓存并刷新", async (_, _) => { widget.Engine.ClearWeatherCache(); await widget.Engine.RefreshWeatherAsync(); status.Text = widget.State.WeatherFailed ? "已清除缓存，30 秒后自动重试" : "天气已刷新"; }));
        panel.Children.Add(Label("天气显示"));
        panel.Children.Add(Label("可组合当前温度、今日温差、小时预报与未来几天。", true));
        var currentCheck = Toggle("当前温度", settings.WeatherCurrent, v => { settings.WeatherCurrent = v; Save(); });
        var rangeCheck = Toggle("今日最高 / 最低温度", settings.WeatherTodayRange, v => { settings.WeatherTodayRange = v; Save(); });
        var hourCheck = Toggle("小时预报", settings.WeatherHourly, v => { settings.WeatherHourly = v; Save(); });
        var dayCheck = Toggle("未来几天", settings.WeatherDaily, v => { settings.WeatherDaily = v; Save(); });
        var presets = new WrapPanel { Margin = new(0, 0, 0, 8) };
        void Preset(bool currentValue, bool range, bool hours, bool days)
        { currentCheck.IsChecked = currentValue; rangeCheck.IsChecked = range; hourCheck.IsChecked = hours; dayCheck.IsChecked = days; }
        presets.Children.Add(ActionButton("只看当前", (_, _) => Preset(true, false, false, false)));
        presets.Children.Add(ActionButton("今日概览", (_, _) => Preset(true, true, false, false)));
        presets.Children.Add(ActionButton("小时预报", (_, _) => Preset(true, true, true, false)));
        presets.Children.Add(ActionButton("多日预报", (_, _) => Preset(true, true, false, true)));
        panel.Children.Add(presets); panel.Children.Add(currentCheck); panel.Children.Add(rangeCheck); panel.Children.Add(hourCheck); panel.Children.Add(dayCheck);
        panel.Children.Add(SliderRow("预报时段数", settings.ForecastHours, 1, 12, 1, "0", v => { settings.ForecastHours = (int)v; Save(); }));
        panel.Children.Add(SliderRow("时段间隔", settings.HourStep, 1, 6, 1, "0' 小时'", v => { settings.HourStep = (int)v; Save(); }));
        panel.Children.Add(Toggle("包含当前小时", settings.HourlyIncludeCurrent, v => { settings.HourlyIncludeCurrent = v; Save(); }));
        panel.Children.Add(SliderRow("未来天数", settings.ForecastDays, 1, 7, 1, "0' 天'", v => { settings.ForecastDays = (int)v; Save(); }));
        panel.Children.Add(Label("小时与日期使用所选城市的当地时间，未来天数从明天开始。\n天气数据：Open-Meteo · CC BY 4.0。离线时保留上次读数。", true)); return panel;
    }
    private UIElement Poetry()
    {
        var panel = new StackPanel { Margin = new(1, 8, 12, 0) };
        panel.Children.Add(Toggle("开启每日诗词", settings.ShowPoetry, v => { settings.ShowPoetry = v; Save(); }));
        panel.Children.Add(Toggle("每天自动更新", settings.PoetryAutoRefresh, v => { settings.PoetryAutoRefresh = v; Save(); }));
        panel.Children.Add(Label("每天更新一条诗词；在线异常时使用已有缓存或 30 条内置公版诗句。关闭后不发起诗词请求。", true));
        panel.Children.Add(ActionButton("刷新诗词", async (_, _) => { if (!settings.ShowPoetry) { status.Text = "请先开启每日诗词"; return; } status.Text = "正在更新诗词…"; await widget.Engine.RefreshPoetryAsync(true); status.Text = widget.State.PoetryFailed ? "在线暂不可用，已使用缓存或内置诗库" : "诗词已更新"; }));
        panel.Children.Add(Label("诗句统一为一行，右下角显示朝代首字与诗名；较长内容适当缩小或省略。完整诗句、朝代、作者与出处可悬停查看。", true));
        panel.Children.Add(Label("来源：今日诗词。服务会根据来源 IP 推测地区用于推荐；手动刷新可能返回同一条缓存推荐。", true));
        return panel;
    }
    private UIElement Todos()
    {
        var panel = new StackPanel { Margin = new(1, 4, 12, 0) };
        panel.Children.Add(Label("记下要做的事。", true));
        panel.Children.Add(Toggle("在组件上显示待办", settings.ShowTodo, v => { settings.ShowTodo = v; Save(); }));
        panel.Children.Add(Toggle("隐藏已完成事项", settings.HideCompletedTodos, v => { settings.HideCompletedTodos = v; Save(); }));
        panel.Children.Add(SliderRow("可见行数", settings.TodoVisibleRows, 1, 8, 1, "0' 行'", v => { settings.TodoVisibleRows = (int)v; Save(); }));
        var input = new TextBox { Padding = new(10), MaxLength = 160, ToolTip = "输入新待办，按 Enter 添加", Background = theme.Brush("#F2F4ED"), BorderBrush = theme.Brush("#DDE2D6") };
        HoverHints.Set(input, "输入新待办，按 Enter 添加");
        panel.Children.Add(input);
        void Add()
        {
            var text = input.Text.Trim(); if (text.Length == 0) return;
            if (settings.Todos.Count >= 200) { status.Text = "最多保存 200 项，请清理已完成事项"; return; }
            settings.Todos.Add(new TodoItem { Text = text }); input.Clear(); Save(); BuildTodoList();
        }
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Add(); e.Handled = true; } };
        var actions = new WrapPanel(); actions.Children.Add(ActionButton("添加待办", (_, _) => Add()));
        actions.Children.Add(ActionButton("清理已完成", (_, _) => { settings.Todos.RemoveAll(t => t.Completed); Save(); BuildTodoList(); }));
        panel.Children.Add(actions); panel.Children.Add(todoList);
        panel.Children.Add(Label("组件中点圆圈即可完成；鼠标移入后显示输入栏，按 Enter 添加。超出可见行数时可滚动。清单仅保存在本机。", true)); return panel;
    }
    private void BuildTodoList()
    {
        todoList.Children.Clear();
        foreach (var item in settings.Todos)
        {
            var row = new Grid { Margin = new(0, 5, 0, 5) }; row.ColumnDefinitions.Add(new() { Width = new(44) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new(45) });
            var done = Toggle("", item.Completed, v => { item.Completed = v; Save(); }); row.Children.Add(done);
            var text = new TextBox { Text = item.Text, MaxLength = 160, Padding = new(9), Background = theme.Brush("#F2F4ED"), BorderBrush = theme.Brush("#DFE3D8") };
            text.LostKeyboardFocus += (_, _) => { if (!string.IsNullOrWhiteSpace(text.Text)) { item.Text = text.Text.Trim(); Save(); } else text.Text = item.Text; };
            Grid.SetColumn(text, 1); row.Children.Add(text);
            var delete = ActionButton("×", (_, _) => { settings.Todos.Remove(item); Save(); BuildTodoList(); }); delete.Padding = new(8, 4, 8, 4); HoverHints.Set(delete, "删除待办"); Grid.SetColumn(delete, 2); row.Children.Add(delete);
            todoList.Children.Add(row);
        }
    }
    private UIElement Devices()
    {
        var panel = new StackPanel { Margin = new(1, 8, 12, 0) };
        panel.Children.Add(Label("自动检测连接方式与设备类型。识别有误时可手动调整。", true));
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(ActionButton("重新检测", async (_, _) => { status.Text = "检测中…"; await widget.Engine.RefreshDevicesAsync(); BuildDevices(); status.Text = "检测完成"; }));
        actions.Children.Add(ActionButton("导出诊断", (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "mizu-diagnostics.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(widget.Engine.Batteries.Report(widget.State.Devices), new JsonSerializerOptions { WriteIndented = true })); status.Text = "诊断已导出，不含推送密钥"; }
            catch (IOException) { status.Text = "文件无法写入"; }
        }));
        panel.Children.Add(actions); panel.Children.Add(deviceList);
        panel.Children.Add(new Separator { Margin = new(0, 15, 0, 12) });
        var temperatureToggle = Toggle("启用 CPU 温度读取", settings.TemperatureEnabled, v => { settings.TemperatureEnabled = v; Save(); });
        panel.Children.Add(temperatureToggle);
        panel.Children.Add(Label("已内置官方签名的 PawnIO 2.2.0 安装包，无需另行下载。点击安装时会请求管理员授权；安装完成后，以管理员身份重启 Mizu 即可读取温度。", true));
        var driverStatus = Label("", true);
        void UpdateDriverStatus()
        {
            try { driverStatus.Text = TemperatureDriver.UnsupportedReason ?? (TemperatureDriver.InstalledVersion is { } version ? $"已安装 PawnIO {version}" : "尚未安装温度驱动 · 支持 x64 Windows 10 1809+ / Windows 11"); }
            catch (Exception ex) { driverStatus.Text = "暂时无法检查温度驱动状态"; AppLog.Write("driver.detect", ex.GetType().Name); }
        }
        UpdateDriverStatus(); panel.Children.Add(driverStatus);
        var install = ActionButton("安装内置温度驱动", async (sender, _) =>
        {
            var button = (Button)sender; button.IsEnabled = false;
            status.Text = "正在安装温度驱动，请完成 Windows 管理员授权…";
            try
            {
                var result = await TemperatureDriver.InstallAsync();
                if (result.State == DriverInstallState.Failed) { status.Text = $"驱动安装未完成（错误 {result.ExitCode}），可查看官方说明后重试"; return; }
                settings.TemperatureEnabled = true; Save();
                status.Text = result.State == DriverInstallState.RebootRequired
                    ? "驱动安装完成，Windows 要求重启电脑；请保存工作后手动重启"
                    : "温度读取已启用；点击“以管理员身份重启”重新识别驱动并读取温度";
                temperatureToggle.IsChecked = true;
                UpdateDriverStatus();
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { status.Text = "已取消安装，稍后可再次点击"; }
            catch (Exception ex) { status.Text = ex is FileNotFoundException ? "缺少内置安装包，请重新完整解压便携包" : ex is InvalidDataException or PlatformNotSupportedException ? ex.Message : "驱动安装未完成，请稍后重试或查看官方说明"; AppLog.Write("driver.install", ex.GetType().Name); }
            finally { button.IsEnabled = true; }
        });
        install.IsEnabled = TemperatureDriver.UnsupportedReason is null; panel.Children.Add(install);
        panel.Children.Add(ActionButton("驱动官方说明", (_, _) => Process.Start(new ProcessStartInfo(TemperatureDriver.ReleaseUrl) { UseShellExecute = true })));
        panel.Children.Add(ActionButton("以管理员身份重启", async (_, _) =>
        {
            try
            {
                // The user explicitly clicks this; Windows owns the UAC approval prompt.
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = "--restart" });
                await widget.QuitAsync();
            }
            catch (System.ComponentModel.Win32Exception) { status.Text = "未重启，当前组件继续运行"; }
        }));
        panel.Children.Add(Label("2.4G 电量由厂商协议决定。未适配设备不会收到猜测的查询命令；可导出诊断以补充硬件适配。", true)); return panel;
    }
    private void BuildDevices()
    {
        deviceList.Children.Clear();
        if (widget.State.Devices.Count == 0) { deviceList.Children.Add(Label(widget.State.Scanning ? "正在检测…" : "尚未检测到可读电量的设备", true)); return; }
        foreach (var device in widget.State.Devices)
        {
            var panel = new StackPanel { Margin = new(0, 10, 0, 12) };
            var name = new TextBox { Text = settings.NameOverrides.GetValueOrDefault(device.Id, device.Name), Padding = new(7), Background = theme.Brush("#FAFAF5"), BorderBrush = theme.Brush("#DBDFD3") };
            name.LostKeyboardFocus += (_, _) => { var text = name.Text.Trim(); if (text.Length > 0) settings.NameOverrides[device.Id] = text[..Math.Min(80, text.Length)]; Save(); }; panel.Children.Add(name);
            panel.Children.Add(Label($"{device.Percent?.ToString() ?? "—"}% · {device.Link} · {device.State}", true));
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var kind = new ComboBox { ItemsSource = new[] { "自动分类", "键盘", "鼠标" }, SelectedIndex = settings.TypeOverrides.TryGetValue(device.Id, out var k) ? (int)k : 0, Width = 135, Padding = new(7), Margin = new(0, 0, 15, 0) };
            kind.SelectionChanged += (_, _) => { if (kind.SelectedIndex == 0) settings.TypeOverrides.Remove(device.Id); else settings.TypeOverrides[device.Id] = (PeripheralKind)kind.SelectedIndex; Save(); }; row.Children.Add(kind);
            row.Children.Add(Toggle("隐藏", settings.HiddenDevices.Contains(device.Id), v => { if (v) settings.HiddenDevices.Add(device.Id); else settings.HiddenDevices.Remove(device.Id); Save(); })); panel.Children.Add(row);
            panel.Children.Add(Label(device.Source, true)); deviceList.Children.Add(panel);
        }
    }
    private UIElement Push()
    {
        var panel = new StackPanel { Margin = new(1, 8, 12, 0) };
        panel.Children.Add(Label("PushPlus"));
        panel.Children.Add(Label("填写你自己的 Token。启用后，仅在达到阈值时发送提醒。", true));
        var token = new PasswordBox { Password = SettingsStore.Unprotect(settings.ProtectedPushToken) ?? "", Padding = new(10), Background = theme.Brush("#FAFAF5"), BorderBrush = theme.Brush("#D8DCCD") }; panel.Children.Add(token);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(ActionButton("保存绑定", (_, _) => { settings.ProtectedPushToken = SettingsStore.Protect(token.Password); Save(); status.Text = token.Password.Length > 0 ? "绑定已保存" : "绑定已清除"; }));
        buttons.Children.Add(ActionButton("发送测试", async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(token.Password)) { status.Text = "请填写 Token"; return; }
            try { await widget.Engine.Notifications.TestAsync(token.Password.Trim(), lifetime.Token); status.Text = "服务器已接受测试请求，请查看接收端"; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = "测试未成功，请检查 Token 和网络"; AppLog.Write("push.test", ex.GetType().Name); }
        })); panel.Children.Add(buttons);
        panel.Children.Add(Toggle("启用阈值提醒", settings.PushEnabled, v => { settings.PushEnabled = v; Save(); }));
        panel.Children.Add(SliderRow("外设低电量", settings.BatteryThreshold, 5, 50, 5, "0'%'", v => { settings.BatteryThreshold = (int)v; Save(); }));
        panel.Children.Add(SliderRow("CPU 负载", settings.CpuThreshold, 50, 100, 5, "0'%'", v => { settings.CpuThreshold = v; Save(); }));
        panel.Children.Add(SliderRow("内存占用", settings.MemoryThreshold, 50, 100, 5, "0'%'", v => { settings.MemoryThreshold = v; Save(); }));
        panel.Children.Add(SliderRow("CPU 温度", settings.TemperatureThreshold, 50, 110, 5, "0'°C'", v => { settings.TemperatureThreshold = v; Save(); }));
        panel.Children.Add(SliderRow("持续时间", settings.SustainedSeconds, 10, 300, 10, "0' 秒'", v => { settings.SustainedSeconds = (int)v; Save(); }));
        panel.Children.Add(SliderRow("提醒间隔", settings.CooldownMinutes, 5, 240, 5, "0' 分'", v => { settings.CooldownMinutes = (int)v; Save(); }));
        panel.Children.Add(Label("低电量即时提醒；负载与温度需持续超限。离线、过期和充电中的设备不触发低电量提醒。\n密钥由当前 Windows 用户加密保存在本机。", true)); return panel;
    }
}
