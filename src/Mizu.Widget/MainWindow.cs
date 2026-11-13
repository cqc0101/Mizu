using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Mizu.Core.Services;
using Mizu.Widget.Controls;
using Mizu.Core;

namespace Mizu.Widget;

public sealed class MainWindow : Window
{
    public AppSettings Settings { get; }
    public WidgetState State { get; }
    public WidgetSurface Surface { get; }
    public WidgetEngine Engine { get; }
    private readonly SettingsStore store;
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly StackPanel toolbar;
    private readonly Grid layout;
    private readonly Grid todoHost = new();
    private readonly ScaleTransform widgetScale = new(1, 1);
    private int resizeGeneration;
    private bool settingsPreviewQueued, fullPreviewNeeded;
    private string? todoKey;
    public FrameworkElement VisualContent => layout;
    private Border? todoEditor;
    private SettingsWindow? settingsWindow;
    private bool quitting;
    private bool disposed;
    private readonly bool persistSettings;
    public MainWindow(AppSettings settings, WidgetState? state = null, bool preview = false, bool persistSettings = true)
    {
        this.persistSettings = persistSettings;
        Settings = settings; State = state ?? new(); store = new();
        var initialLayout = WidgetLayout.Calculate(settings);
        Title = "Mizu"; Width = 356 * settings.Scale; Height = initialLayout.Height * settings.Scale;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = settings.AlwaysOnTop;
        UseLayoutRounding = false;
        HorizontalContentAlignment = HorizontalAlignment.Left; VerticalContentAlignment = VerticalAlignment.Top;
        Surface = new(State, Settings) { Width = 356, Height = initialLayout.Height, PreviewMaterial = preview };
        System.Windows.Automation.AutomationProperties.SetName(Surface, "天气、外设电量与系统状态");
        layout = new Grid { Width = 356, Height = initialLayout.Height,
            Clip = new RectangleGeometry(new Rect(0, 0, 356, initialLayout.Height), settings.CornerRadius, settings.CornerRadius) };
        layout.Children.Add(Surface);
        layout.Children.Add(todoHost);
        toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 5, 24, 0), Opacity = 0, IsHitTestVisible = false };
        toolbar.Children.Add(IconButton("refresh", "刷新 · F5", async () => { await RefreshAsync(); }));
        toolbar.Children.Add(IconButton("pin", "切换置顶", () => { Settings.AlwaysOnTop = !Settings.AlwaysOnTop; ApplySettings(); SaveSettings(); }));
        toolbar.Children.Add(IconButton("settings", "设置 · Ctrl+,", OpenSettings));
        toolbar.Children.Add(IconButton("close", "收起到托盘", Hide));
        layout.Children.Add(toolbar);
        layout.RenderTransform = widgetScale;
        var stage = new Canvas(); stage.Children.Add(layout); Content = stage;
        Engine = new(settings, State, () => Surface.InvalidateVisual());
        tray = new() { Text = "Mizu", Visible = !preview };
        using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)) if (icon is not null) tray.Icon = (System.Drawing.Icon)icon.Clone();
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 收起", null, (_, _) => ToggleVisible());
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("穿透鼠标 · Ctrl+Alt+M 恢复", null, (_, _) => Dispatcher.Invoke(() => { Settings.ClickThrough = !Settings.ClickThrough; ApplySettings(); SaveSettings(); }));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(async () => await QuitAsync()));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleVisible);
        SourceInitialized += (_, _) =>
        {
            ApplySettings();
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
            if (!preview && !NativeWindow.RegisterHotKey(hwnd, 0x4d5a, 0x4003, 0x4d)) AppLog.Write("hotkey", "Ctrl+Alt+M unavailable; tray recovery remains available");
        };
        Loaded += (_, _) =>
        {
            RestorePosition();
            if (!preview) Engine.Start();
        };
        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) => SetHover(false);
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInteractive(source)) return;
            if (e.ClickCount == 2) { OpenSettings(); return; }
            try { DragMove(); Settings.Left = Left; Settings.Top = Top; SaveSettings(); } catch (InvalidOperationException) { }
        };
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.F5) { await RefreshAsync(); e.Handled = true; }
            if (e.Key == Key.OemComma && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { OpenSettings(); e.Handled = true; }
            if (e.Key == Key.Escape) Hide();
        };
        Closing += (_, e) => { if (!quitting) { e.Cancel = true; Hide(); } };
    }
    private void SetHover(bool visible)
    {
        toolbar.IsHitTestVisible = visible;
        if (!visible)
        {
            HoverHints.Close(Surface);
            foreach (var button in toolbar.Children.OfType<Button>()) HoverHints.Close(button);
        }
        toolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(visible ? 160 : 220)));
        if (todoEditor is not null && !todoEditor.IsKeyboardFocusWithin)
        { todoEditor.IsHitTestVisible = visible; todoEditor.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(160))); }
    }
    private bool IsInteractive(DependencyObject source)
    {
        while (source is not null)
        {
            if (source == toolbar || source == todoHost || source is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }
    private static Button IconButton(string icon, string tip, Action clicked)
    {
        var button = new Button { Width = 29, Height = 25, Background = Brushes.Transparent, BorderThickness = new(0), Cursor = Cursors.Hand,
            Content = new IconView(icon, Art.Brush("#8A9386")) { Width = 13, Height = 13 }, Padding = new(6), FocusVisualStyle = null };
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        HoverHints.Set(button, tip);
        button.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
              <Border x:Name="Body" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" CornerRadius="7" Background="Transparent"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Body" Property="Background" Value="#2486937F"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate>
            """);
        button.Click += (_, _) => clicked(); return button;
    }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == 0x4d5a)
        { Settings.ClickThrough = false; Show(); ApplySettings(); SaveSettings(); Activate(); handled = true; }
        if (msg == 0x8001) { Show(); Activate(); handled = true; }
        if (msg == 0x0219 || msg == 0x0218 && wParam.ToInt32() == 0x12) _ = Engine.RefreshDevicesAsync();
        return IntPtr.Zero;
    }
    private void RestorePosition()
    {
        var work = SystemParameters.WorkArea;
        Left = Settings.Left ?? work.Right - Width - 40; Top = Settings.Top ?? work.Top + 50;
        var dpi = VisualTreeHelper.GetDpi(this);
        var rectangle = new System.Drawing.Rectangle((int)(Left * dpi.DpiScaleX), (int)(Top * dpi.DpiScaleY), (int)(Width * dpi.DpiScaleX), (int)(Height * dpi.DpiScaleY));
        if (!System.Windows.Forms.Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rectangle))) { Left = work.Right - Width - 30; Top = work.Top + 30; }
    }
    public void ApplySettings()
    {
        HoverHints.SetPalette(this, ThemePalette.Get(Settings.Theme));
        HoverHints.SetIsDark(this, ThemePalette.Get(Settings.Theme).Dark);
        var calculated = WidgetLayout.Calculate(Settings);
        var heightChanged = Math.Abs(layout.Height - calculated.Height) > .01;
        Topmost = Settings.AlwaysOnTop;
        layout.Height = Surface.Height = calculated.Height;
        layout.Clip = new RectangleGeometry(new Rect(0, 0, 356, calculated.Height), Settings.CornerRadius, Settings.CornerRadius);
        toolbar.Margin = new(0, 6, Math.Max(24, Settings.CornerRadius * .65), 0);
        var area = calculated.Sections.FirstOrDefault(s => s.Kind == WidgetSection.Todo);
        var key = $"{area}|{Settings.Theme}|{Settings.TextScale}|{Settings.TextWeight}|{Settings.HideCompletedTodos}|" + string.Join("\n", Settings.Todos.Select(t => $"{t.Id}:{t.Completed}:{t.Text}"));
        if (key != todoKey) { BuildTodo(calculated); todoKey = key; }
        Surface.InvalidateVisual(); NativeWindow.Apply(this, Settings);
        ResizeWidget(!heightChanged);
    }
    public void RequestSettingsPreview(bool scaleOnly = false)
    {
        fullPreviewNeeded |= !scaleOnly;
        if (settingsPreviewQueued) return;
        settingsPreviewQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            settingsPreviewQueued = false; var full = fullPreviewNeeded; fullPreviewNeeded = false;
            if (disposed) return;
            if (full) ApplySettings(); else ResizeWidget(true);
        }));
    }
    private void ResizeWidget(bool animate)
    {
        double target = Math.Min(Settings.Scale, Math.Max(.3, (SystemParameters.WorkArea.Height - 40) / layout.Height));
        double start = widgetScale.ScaleX; int generation = ++resizeGeneration;
        double width = 356 * target, height = layout.Height * target;
        void Finish()
        {
            if (generation != resizeGeneration) return;
            widgetScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); widgetScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            widgetScale.ScaleX = widgetScale.ScaleY = target; layout.CacheMode = null;
            Width = width; Height = height;
            if (IsLoaded && Top < SystemParameters.WorkArea.Bottom && Top + height > SystemParameters.WorkArea.Bottom)
                Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - height - 12);
        }
        if (!animate || !IsLoaded || !Settings.SmoothResize || Math.Abs(start - target) < .0001) { Finish(); return; }
        // Resize the transparent HWND once to hold both sizes. Animate only the
        // content transform, then shrink the bounds and restore vector rendering.
        double reserveScale = Math.Min(1.5, Math.Max(.3, (SystemParameters.WorkArea.Height - 40) / layout.Height));
        Width = Math.Max(Width, 356 * reserveScale); Height = Math.Max(Height, layout.Height * reserveScale);
        layout.CacheMode ??= new BitmapCache(1.5);
        var motion = new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        motion.Completed += (_, _) => Finish();
        widgetScale.BeginAnimation(ScaleTransform.ScaleXProperty, motion, HandoffBehavior.SnapshotAndReplace);
        widgetScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(start, target, motion.Duration)
        { EasingFunction = motion.EasingFunction }, HandoffBehavior.SnapshotAndReplace);
    }
    private void BuildTodo(WidgetLayout calculated)
    {
        HoverHints.CloseTree(todoHost);
        todoHost.Children.Clear(); todoEditor = null;
        var area = calculated.Sections.FirstOrDefault(s => s.Kind == WidgetSection.Todo);
        todoHost.Visibility = area is null ? Visibility.Collapsed : Visibility.Visible;
        if (area is null) return;
        todoHost.VerticalAlignment = VerticalAlignment.Top; todoHost.HorizontalAlignment = HorizontalAlignment.Left;
        todoHost.Margin = new(30, area.Top, 0, 0); todoHost.Width = 296; todoHost.Height = area.Height;
        var palette = ThemePalette.Get(Settings.Theme); var dark = palette.Dark;
        var rows = new StackPanel();
        foreach (var item in Settings.Todos.Where(t => !Settings.HideCompletedTodos || !t.Completed))
        {
            var label = new TextBlock { Text = item.Text, TextTrimming = TextTrimming.CharacterEllipsis, Width = 258, FontSize = 13 * Settings.TextScale, FontWeight = FontWeight.FromOpenTypeWeight(Settings.TextWeight),
                Foreground = Art.Brush(item.Completed ? palette.Quiet : palette.Text), TextDecorations = item.Completed ? TextDecorations.Strikethrough : null };
            var check = new CheckBox { Content = label, IsChecked = item.Completed, Height = 32, VerticalContentAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            check.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
                  <StackPanel Orientation="Horizontal"><Grid Width="16" Height="16"><Ellipse x:Name="Circle" Stroke="#96A28E" StrokeThickness="1"/><Path x:Name="Tick" Data="M 4,8 L 7,11 12,5" Stroke="#F6F7F1" StrokeThickness="1.5" Visibility="Collapsed"/></Grid><ContentPresenter Margin="12,0,0,0" VerticalAlignment="Center"/></StackPanel>
                  <ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Circle" Property="Fill" Value="#8B9E80"/><Setter TargetName="Tick" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers>
                </ControlTemplate>
                """);
            check.Click += (_, _) => { item.Completed = check.IsChecked == true; ApplySettings(); SaveSettings(); };
            rows.Children.Add(check);
        }
        todoHost.Children.Add(new ScrollViewer { Content = rows, Height = area.Height - 40, VerticalAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var input = new TextBox { Background = Brushes.Transparent, BorderThickness = new(0), Foreground = Art.Brush(palette.Text), CaretBrush = Art.Brush(palette.Accent), FontSize = 12 * Settings.TextScale, FontWeight = FontWeight.FromOpenTypeWeight(Settings.TextWeight), Padding = new(5), MaxLength = 160, ToolTip = "输入待办，按 Enter 添加" };
        HoverHints.Set(input, "输入待办，按 Enter 添加");
        var inputRow = new Grid(); inputRow.ColumnDefinitions.Add(new()); inputRow.ColumnDefinitions.Add(new() { Width = new(30) }); inputRow.Children.Add(input);
        var add = IconButton("plus", "添加待办", () => AddTodo(input)); Grid.SetColumn(add, 1); inputRow.Children.Add(add);
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddTodo(input); e.Handled = true; } };
        todoEditor = new Border { Child = inputRow, VerticalAlignment = VerticalAlignment.Bottom, Height = 30, CornerRadius = new(8), Background = Art.Brush(dark ? "#12FFFFFF" : "#408D9C83"), Opacity = IsMouseOver ? 1 : 0, IsHitTestVisible = IsMouseOver };
        todoHost.Children.Add(todoEditor);
        input.LostKeyboardFocus += (_, _) => { if (!IsMouseOver) SetHover(false); };
    }
    private void AddTodo(TextBox input)
    {
        var text = input.Text.Trim(); if (text.Length == 0 || Settings.Todos.Count >= 200) return;
        Settings.Todos.Add(new TodoItem { Text = text }); ApplySettings(); SaveSettings();
    }
    public void SaveSettings()
    {
        if (!persistSettings) return;
        try { store.Save(Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("settings.save", ex.GetType().Name); }
    }
    public void OpenSettings()
    {
        Settings.ClickThrough = false; ApplySettings();
        if (settingsWindow is { IsLoaded: true }) { settingsWindow.Activate(); return; }
        settingsWindow = new(this); settingsWindow.Closed += (_, _) => settingsWindow = null; settingsWindow.Show();
    }
    public async Task RefreshAsync()
    { await Task.WhenAll(Engine.RefreshDevicesAsync(), Engine.RefreshWeatherAsync()); }
    public void ToggleVisible() => Dispatcher.Invoke(() => { if (IsVisible) Hide(); else { Show(); Activate(); } });
    public async Task QuitAsync()
    {
        if (disposed) return; disposed = true; quitting = true;
        settingsWindow?.Close(); SaveSettings();
        tray.Visible = false; tray.Dispose();
        NativeWindow.UnregisterHotKey(new WindowInteropHelper(this).Handle, 0x4d5a);
        Hide(); await Engine.DisposeAsync(); Close(); Application.Current.Shutdown();
    }
}
