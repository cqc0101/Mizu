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
    public event Action? SettingsApplied;
    private static readonly DependencyProperty LayoutProgressProperty = DependencyProperty.Register("LayoutProgress", typeof(double), typeof(MainWindow), new PropertyMetadata(1.0, (d, _) => ((MainWindow)d).UpdateLayoutTransition()));
    private WidgetLayout? layoutFrom, layoutTo, appliedLayout;
    private bool layoutAnimating;
    private int layoutGeneration;
    public WidgetState State { get; }
    public WidgetSurface Surface { get; }
    public WidgetEngine Engine { get; }
    public UpdateCoordinator Updates { get; }
    internal CloudBackupCoordinator Backups { get; }
    private readonly SettingsStore store;
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly StackPanel toolbar;
    private readonly Button lockButton;
    private readonly Grid layout;
    private readonly Grid mediaHost = new();
    private string? mediaKey;
    private readonly Grid todoHost = new();
    private readonly Grid todoEditorHost = new();
    private readonly System.Windows.Threading.DispatcherTimer todoHideDelay = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private bool footerAnimating, footerWanted;
    private int footerGeneration;
    public static readonly DependencyProperty TodoRevealProperty = DependencyProperty.Register(nameof(TodoReveal), typeof(double), typeof(MainWindow),
        new PropertyMetadata(0.0, (d, _) => ((MainWindow)d).UpdateTodoGeometry()));
    public double TodoReveal { get => (double)GetValue(TodoRevealProperty); private set => SetValue(TodoRevealProperty, value); }
    private static readonly DependencyProperty EditorExpansionProperty = DependencyProperty.Register("EditorExpansion", typeof(double), typeof(MainWindow), new PropertyMetadata(0.0, (d, _) => ((MainWindow)d).UpdateTodoGeometry()));
    private double EditorExpansion { get => (double)GetValue(EditorExpansionProperty); set => SetValue(EditorExpansionProperty, value); }
    private bool editorAnimating;
    private double FooterHeight => 44 + 38 * EditorExpansion;
    private readonly ScaleTransform widgetScale = new(1, 1);
    private int resizeGeneration;
    private bool settingsPreviewQueued, fullPreviewNeeded;
    private bool workAreaRefreshQueued, movingWindow;
    private string? todoKey;
    public FrameworkElement VisualContent => layout;
    private Border? todoEditor;
    private SettingsWindow? settingsWindow;
    private HardwareHistoryWindow? historyWindow;
    private WordLookupPopup? wordLookup;
    private bool quitting;
    private IntPtr hidNotification;
    private HashSet<string> onlineDevices = [];
    private bool disposed;
    private readonly bool persistSettings;
    private bool repositionQueued;
    private string footerMode = "";
    private string addActionsKey = "";
    private bool HasAddActions => Settings.ShowTodo || Settings.ShowVocabulary;
    private readonly System.Windows.Threading.DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ThemeRotation themeRotation = new();
    public MainWindow(AppSettings settings, WidgetState? state = null, bool preview = false, bool persistSettings = true)
    {
        this.persistSettings = persistSettings;
        Settings = settings; State = state ?? new(); store = new();
        var workArea = SystemParameters.WorkArea;
        var initialLayout = WidgetLayout.Calculate(settings, workAreaHeight: workArea.Height, workAreaWidth: workArea.Width);
        Title = "Mizu"; Icon = AppIcons.WindowIcon; Width = initialLayout.Width * settings.Scale; Height = initialLayout.Height * settings.Scale;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = settings.AlwaysOnTop;
        UseLayoutRounding = false;
        HorizontalContentAlignment = HorizontalAlignment.Left; VerticalContentAlignment = VerticalAlignment.Top;
        Surface = new(State, Settings) { Width = initialLayout.Width, Height = initialLayout.Height, PreviewMaterial = preview,
            WorkAreaHeight = workArea.Height, WorkAreaWidth = workArea.Width };
        System.Windows.Automation.AutomationProperties.SetName(Surface, "天气、外设电量与系统状态");
        layout = new Grid { Width = initialLayout.Width, Height = initialLayout.Height,
            Clip = new RectangleGeometry(new Rect(0, 0, initialLayout.Width, initialLayout.Height), settings.CornerRadius, settings.CornerRadius) };
        layout.Children.Add(Surface);
        layout.Children.Add(todoHost);
        layout.Children.Add(todoEditorHost);
        layout.Children.Add(mediaHost);
        toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 5, 24, 0), Opacity = 0, IsHitTestVisible = false };
        toolbar.Children.Add(IconButton("search", "本地查词", ToggleWordLookup));
        toolbar.Children.Add(IconButton("refresh", "刷新 · F5", async () => { await RefreshAsync(); }));
        toolbar.Children.Add(IconButton("pin", "切换置顶", () => { Settings.AlwaysOnTop = !Settings.AlwaysOnTop; ApplySettings(); SaveSettings(); }));
        toolbar.Children.Add(IconButton("settings", "设置 · Ctrl+,", OpenSettings));
        toolbar.Children.Add(IconButton("close", "收起到托盘", Hide));
        lockButton = IconButton("unlock", "锁定面板", () => {
            Settings.PanelLocked = !Settings.PanelLocked;
            if (Settings.PanelLocked) { Keyboard.ClearFocus(); settingsWindow?.Close(); historyWindow?.Close(); }
            ApplySettings(); SaveSettings();
        });
        toolbar.Children.Add(lockButton);
        layout.Children.Add(toolbar);
        layout.RenderTransform = widgetScale;
        var stage = new Canvas(); stage.Children.Add(layout); Content = stage;
        Engine = new(settings, State, OnStateChanged);
        Updates = new(this, !preview && persistSettings);
        Backups = new(this);
        tray = new() { Text = "Mizu", Icon = AppIcons.CreateTrayIcon(), Visible = !preview };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示 / 收起", null, (_, _) => ToggleVisible());
        menu.Items.Add("设置", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("硬件历史", null, (_, _) => Dispatcher.Invoke(() => OpenHardwareHistory()));
        menu.Items.Add("穿透鼠标 · Ctrl+Alt+M 恢复", null, (_, _) => Dispatcher.Invoke(() => { Settings.ClickThrough = !Settings.ClickThrough; ApplySettings(); SaveSettings(); }));
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(async () => await QuitAsync()));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleVisible);
        tray.BalloonTipClicked += (_, _) => Dispatcher.Invoke(() => { OpenSettings(); settingsWindow?.ShowUpdates(); });
        SourceInitialized += (_, _) =>
        {
            UpdateWorkArea();
            ApplySettings();
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!preview) hidNotification = NativeWindow.WatchHid(hwnd);
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
            if (!preview && !NativeWindow.RegisterHotKey(hwnd, 0x4d5a, 0x4003, 0x4d)) AppLog.Write("hotkey", "Ctrl+Alt+M unavailable; tray recovery remains available");
        };
        Loaded += (_, _) =>
        {
            RestorePosition();
            if (UpdateWorkArea()) ApplySettings();
            else if (appliedLayout is { VocabularyRows: > 0 } geometry) KeepVocabularyInWorkArea(geometry, Settings.Scale);
            if (!preview) Engine.Start();
            if (!preview) displayTimer.Start();
            Updates.Start();
            Backups.Start();
        };
        LocationChanged += (_, _) => QueueWorkAreaRefresh();
        DpiChanged += (_, _) => QueueWorkAreaRefresh();
        Deactivated += (_, _) => CloseWordLookup();
        displayTimer.Tick += (_, _) => {
            if (themeRotation.Update(Settings, DateTimeOffset.UtcNow, ThemePalette.All.Select(p => p.Id))) { ApplySettings(); SaveSettings(); }
            if (IsVisible && Settings.ShowDevices) OnStateChanged();
            else if (IsVisible && (Settings.ShowClock || Settings.ShowVocabulary)) Surface.InvalidateVisual();
        };
        todoHideDelay.Tick += (_, _) => { todoHideDelay.Stop(); if (!IsMouseOver && todoEditor?.IsKeyboardFocusWithin != true) AnimateTodoFooter(false); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) return;
            CloseWordLookup();
            todoHideDelay.Stop();
            AnimateTodoFooter(false, false);
        };
        MouseEnter += (_, _) => SetHover(true);
        MouseLeave += (_, _) => SetHover(false);
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInteractive(source)) return;
            if (Settings.PanelLocked) { e.Handled = true; return; }
            var point = e.GetPosition(Surface);
            if (e.OriginalSource == Surface && Surface.TrySpeakVocabulary(point)) { e.Handled = true; return; }
            var hardwareArea = Surface.Layout.Sections.FirstOrDefault(s => point.Y >= s.Top && point.Y < s.Top + s.Height && s.Opacity >= .9);
            if (hardwareArea?.Kind is WidgetSection.Metrics or WidgetSection.GpuMemory)
            {
                var metrics = new[] { HardwareMetric.Cpu, HardwareMetric.Memory, HardwareMetric.CpuTemperature }.Where(m => HardwareMetricInfo.Enabled(m, Settings)).ToArray();
                double x = point.X - Surface.SectionInset;
                HardwareMetric? metric = hardwareArea.Kind == WidgetSection.GpuMemory ? Surface.GpuMetricAt(x) :
                    metrics.Length == 0 ? null : metrics[Math.Clamp((int)((x - 30) / (300.0 / metrics.Length)), 0, metrics.Length - 1)];
                var origin = new Point(Left, Top);
                try { DragMove(); } catch (InvalidOperationException) { }
                Settings.Left = Left; Settings.Top = Top; SaveSettings();
                if (Mouse.LeftButton == MouseButtonState.Released && Math.Abs(Left - origin.X) < 2 && Math.Abs(Top - origin.Y) < 2)
                    OpenHardwareHistory(metric);
                e.Handled = true; return;
            }
            if (e.ClickCount == 2) { OpenSettings(); return; }
            try { DragMove(); Settings.Left = Left; Settings.Top = Top; SaveSettings(); } catch (InvalidOperationException) { }
        };
        PreviewKeyDown += async (_, e) =>
        {
            if (Settings.PanelLocked) { e.Handled = true; return; }
            if (e.Key == Key.F5) { await RefreshAsync(); e.Handled = true; }
            if (e.Key == Key.OemComma && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { OpenSettings(); e.Handled = true; }
            if (e.Key == Key.Escape)
            {
                if (wordLookup?.IsOpen == true) CloseWordLookup();
                else Hide();
                e.Handled = true;
            }
        };
        Closing += (_, e) => { if (!quitting) { e.Cancel = true; Hide(); } };
    }
    private void SetHover(bool visible)
    {
        SetToolbarHover(visible || wordLookup?.IsOpen == true);
        if (!visible) HoverHints.Close(Surface);
        if (visible && !Settings.PanelLocked) { todoHideDelay.Stop(); AnimateTodoFooter(true); }
        else if (todoEditor?.IsKeyboardFocusWithin != true) { todoHideDelay.Stop(); todoHideDelay.Start(); }
    }
    private void SetToolbarHover(bool visible)
    {
        toolbar.IsHitTestVisible = visible;
        if (!visible)
        {
            foreach (var button in toolbar.Children.OfType<Button>()) HoverHints.Close(button);
        }
        toolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(visible ? 160 : 220)));
    }
    private void ToggleWordLookup()
    {
        if (!IsVisible || Settings.PanelLocked || Settings.ClickThrough) return;
        if (wordLookup is null)
        {
            wordLookup = new(Settings, toolbar);
            wordLookup.Opened += (_, _) => SetToolbarHover(true);
            wordLookup.Closed += (_, _) => SetToolbarHover(IsVisible && IsMouseOver);
        }
        foreach (var button in toolbar.Children.OfType<Button>()) HoverHints.Close(button);
        wordLookup.IsOpen = !wordLookup.IsOpen;
    }
    private void CloseWordLookup()
    {
        if (wordLookup is { IsOpen: true } popup) popup.IsOpen = false;
    }
    private void OnStateChanged()
    {
        var visible = Surface.VisibleDevices;
        var current = visible.Where(d => d.State == LinkState.Online).Select(d => d.Id).ToHashSet();
        var added = visible.FirstOrDefault(d => current.Contains(d.Id) && !onlineDevices.Contains(d.Id))?.Id; onlineDevices = current;
        if (added is not null) State.DevicePage = Math.Max(0, visible.ToList().FindIndex(d => d.Id == added) / 2);
        var target = Surface.TargetLayout;
        if (LayoutChanged(target))
        {
            ApplySettings();
        }
        Surface.InvalidateVisual(); UpdateMediaControls();
    }
    private bool GeometryChanged(WidgetLayout target) => appliedLayout is null ||
        appliedLayout.Width != target.Width || !appliedLayout.Sections.SequenceEqual(target.Sections);
    private bool LayoutChanged(WidgetLayout target) => GeometryChanged(target) ||
        appliedLayout!.VocabularyColumns != target.VocabularyColumns || appliedLayout.VocabularyRowLimit != target.VocabularyRowLimit;
    private void StartLayoutTransition(WidgetLayout from, WidgetLayout to)
    {
        int generation = ++layoutGeneration;
        layoutFrom = from; layoutTo = to; layoutAnimating = true;
        Width = Math.Max(Width, Math.Max(from.Width, to.Width) * widgetScale.ScaleX);
        Height = Math.Max(Height, (Math.Max(from.Height, to.Height) + FooterHeight) * widgetScale.ScaleY);
        SetValue(LayoutProgressProperty, 0.0);
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        animation.Completed += (_, _) => {
            if (generation != layoutGeneration) return;
            SetValue(LayoutProgressProperty, 1.0); BeginAnimation(LayoutProgressProperty, null);
            Surface.TransitionLayout = null; layoutAnimating = false; UpdateTodoGeometry(); UpdateSectionControls();
        };
        BeginAnimation(LayoutProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
        UpdateLayoutTransition();
    }
    private void UpdateLayoutTransition()
    {
        if (!layoutAnimating || layoutFrom is null || layoutTo is null) return;
        double p = (double)GetValue(LayoutProgressProperty);
        Surface.TransitionLayout = WidgetLayout.Interpolate(layoutFrom, layoutTo, p);
        UpdateTodoGeometry(); UpdateSectionControls();
    }
    private void UpdateSectionControls()
    {
        var todo = Surface.Layout.Sections.FirstOrDefault(s => s.Kind == WidgetSection.Todo);
        todoHost.Opacity = todo?.Opacity ?? 0;
        todoHost.IsHitTestVisible = !Settings.PanelLocked && todo?.Opacity >= .9;
        if (todo is not null)
        {
            todoHost.Margin = new(30 + Surface.SectionInset, todo.Top, 0, 0);
            todoHost.Height = todo.Height;
            todoHost.ClipToBounds = true;
        }
        UpdateMediaControls();
    }
    private void AnimateTodoFooter(bool open, bool animate = true)
    {
        open &= HasAddActions && !Settings.PanelLocked;
        if (animate && footerAnimating && footerWanted == open) { UpdateTodoGeometry(); return; }
        footerWanted = open;
        if (open) todoHideDelay.Stop();
        double target = open ? 1 : 0, from = TodoReveal;
        if (Math.Abs(from - target) < .0001 && !footerAnimating) { UpdateTodoGeometry(); return; }
        int generation = ++footerGeneration;
        void Finish() {
            if (generation != footerGeneration) return;
            // Set the underlying value before removing the animation clock.
            // Otherwise WPF exposes the old base value for one change callback,
            // briefly shrinking the native window before expanding it again.
            TodoReveal = target; BeginAnimation(TodoRevealProperty, null);
            footerAnimating = false; UpdateTodoGeometry();
            if (target == 0 && !disposed && Settings.ShowVocabulary && footerMode != "") { footerMode = ""; BuildAddEditor(); }
        }
        if (!animate || !IsLoaded || !Settings.SmoothResize) { Finish(); return; }
        footerAnimating = true;
        // Reserve native bounds once; only the WPF surface, clip and editor
        // move each frame. A reversed gesture starts from the current value.
        Height = Math.Max(Height, (Surface.Layout.Height + FooterHeight) * widgetScale.ScaleY);
        var motion = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(open ? 260 : 220)) {
            EasingFunction = open ? new QuinticEase { EasingMode = EasingMode.EaseOut } : new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        motion.Completed += (_, _) => Finish(); BeginAnimation(TodoRevealProperty, motion, HandoffBehavior.SnapshotAndReplace);
    }
    private void UpdateTodoGeometry()
    {
        if (layout is null || Surface is null) return;
        var geometry = Surface.Layout;
        var baseHeight = geometry.Height;
        var reveal = HasAddActions ? Math.Clamp(TodoReveal, 0, 1) : 0;
        var height = baseHeight + FooterHeight * reveal;
        layout.Width = Surface.Width = geometry.Width;
        layout.Height = Surface.Height = height; Surface.FooterHeight = FooterHeight; Surface.FooterReveal = reveal; Surface.InvalidateVisual();
        todoEditorHost.Height = 30 + 38 * EditorExpansion; todoEditorHost.ClipToBounds = true;
        layout.Clip = new RectangleGeometry(new Rect(0, 0, geometry.Width, height), Settings.CornerRadius, Settings.CornerRadius);
        todoEditorHost.Margin = new(30 + Surface.SectionInset, baseHeight - 4, 0, 0);
        todoEditorHost.Opacity = Math.Clamp((reveal - .12) / .88, 0, 1);
        todoEditorHost.RenderTransform = new TranslateTransform(0, 8 * (1 - reveal));
        todoEditorHost.IsHitTestVisible = !Settings.PanelLocked && footerWanted && reveal > .85;
        if (!footerAnimating && !editorAnimating && !layoutAnimating && !widgetScale.HasAnimatedProperties) { Width = geometry.Width * widgetScale.ScaleX; Height = height * widgetScale.ScaleY; }
    }
    private bool IsInteractive(DependencyObject source)
    {
        while (source is not null)
        {
            if (source == toolbar || source == todoHost || source == todoEditorHost || source is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.TextBoxBase) return true;
            source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return false;
    }
    private Button IconButton(string icon, string tip, Action clicked)
    {
        var button = new Button { Width = 29, Height = 25, Background = Brushes.Transparent, BorderThickness = new(0), Cursor = Cursors.Hand,
            Content = new IconView(icon, Surface.Material.Secondary) { Width = 13, Height = 13 }, Padding = new(6), FocusVisualStyle = null };
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
        if (msg == 0x007e || msg == 0x001a && wParam.ToInt64() == 0x2f)
        {
            QueueEnsureReachablePosition();
            QueueWorkAreaRefresh();
        }
        if (msg == 0x0231) movingWindow = true; // WM_ENTERSIZEMOVE
        if (msg == 0x0232)
        {
            movingWindow = false;
            QueueWorkAreaRefresh();
        }
        if (msg == 0x0312 && wParam.ToInt32() == 0x4d5a)
        { ShowInteractive(); handled = true; }
        if (msg == 0x8001)
        {
            ShowInteractive(); handled = true;
        }
        if (msg == 0x0219 || msg == 0x0218 && wParam.ToInt32() == 0x12) Engine.RequestConnectionRefresh();
        return IntPtr.Zero;
    }
    private void RestorePosition()
    {
        var work = SystemParameters.WorkArea;
        Left = Settings.Left ?? work.Right - Width - 40; Top = Settings.Top ?? work.Top + 50;
        EnsureReachablePosition();
    }
    private void QueueWorkAreaRefresh()
    {
        if (!IsLoaded || movingWindow || workAreaRefreshQueued || disposed) return;
        workAreaRefreshQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            workAreaRefreshQueued = false;
            if (!disposed && !movingWindow && UpdateWorkArea()) ApplySettings();
        }));
    }
    private bool UpdateWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return false;
        var work = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double width = work.Width / dpi.DpiScaleX, height = work.Height / dpi.DpiScaleY;
        if (Math.Abs(height - Surface.WorkAreaHeight) < .1 && Math.Abs(width - Surface.WorkAreaWidth) < .1) return false;
        Surface.WorkAreaWidth = width; Surface.WorkAreaHeight = height;
        return true;
    }
    private void KeepVocabularyInWorkArea(WidgetLayout target, double scale)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (movingWindow || hwnd == IntPtr.Zero || !NativeWindow.GetWindowRect(hwnd, out var bounds)) return;
        var work = System.Windows.Forms.Screen.FromHandle(hwnd).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        int width = (int)Math.Ceiling(target.Width * scale * dpi.DpiScaleX);
        int height = (int)Math.Ceiling((target.Height + WidgetLayout.ExpandedQuickEntryHeight) * scale * dpi.DpiScaleY);
        int x = Math.Clamp(bounds.Left, work.Left, Math.Max(work.Left, work.Right - width));
        int y = Math.Clamp(bounds.Top, work.Top, Math.Max(work.Top, work.Bottom - height));
        if (x != bounds.Left || y != bounds.Top) NativeWindow.MoveToPhysicalPosition(hwnd, x, y);
    }
    private void QueueEnsureReachablePosition()
    {
        if (repositionQueued || disposed) return;
        repositionQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
        { repositionQueued = false; EnsureReachablePosition(); }));
    }
    private void EnsureReachablePosition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !NativeWindow.GetWindowRect(hwnd, out var bounds)) return;
        if (HasReachableWorkArea(bounds, System.Windows.Forms.Screen.AllScreens.Select(screen => screen.WorkingArea))) return;
        // WPF coordinates are DPI-scaled; place the native HWND in physical
        // primary-monitor coordinates so detached displays cannot strand it.
        var work = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
        if (work is null || !NativeWindow.MoveToPhysicalPosition(hwnd, work.Value.Left + 28, work.Value.Top + 28)) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        { Settings.Left = Left; Settings.Top = Top; SaveSettings(); }));
    }
    internal static bool HasReachableWorkArea(System.Drawing.Rectangle window, IEnumerable<System.Drawing.Rectangle> workAreas) =>
        workAreas.Any(work => {
            var area = System.Drawing.Rectangle.Intersect(window, work);
            return area.Width >= Math.Min(96, window.Width) && area.Height >= Math.Min(96, window.Height);
        });
    public void ApplySettings(bool animateScale = false)
    {
        var previous = Surface.TransitionLayout ?? appliedLayout;
        layoutGeneration++;
        layoutAnimating = false; BeginAnimation(LayoutProgressProperty, null); Surface.TransitionLayout = null;
        State.HardwareHistory.ApplySettings(Settings);
        Updates.Configure();
        SettingsApplied?.Invoke();
        HoverHints.SetPalette(this, ThemePalette.Get(Settings.Theme));
        HoverHints.SetIsDark(this, ThemePalette.Get(Settings.Theme).Dark);
        var calculated = Surface.TargetLayout; appliedLayout = calculated;
        bool transition = previous is not null && IsLoaded && Settings.SmoothResize &&
            (previous.Width != calculated.Width || !previous.Sections.SequenceEqual(calculated.Sections));
        layoutAnimating = transition;
        if (transition) Surface.TransitionLayout = previous;
        var heightChanged = Math.Abs(layout.Height - calculated.Height) > .01;
        Topmost = Settings.AlwaysOnTop;
        layout.Height = Surface.Height = calculated.Height;
        toolbar.Margin = new(0, 6, Math.Max(24, Settings.CornerRadius * .65), 0);
        var area = calculated.Sections.FirstOrDefault(s => s.Kind == WidgetSection.Todo);
        var key = $"{Settings.ShowTodo}|{Settings.ShowVocabulary}|{calculated.Height}|{area}|{Settings.Theme}|{Settings.TextScale}|{Settings.TextWeight}|{Settings.HideCompletedTodos}|" + string.Join("\n", Settings.Todos.Select(t => $"{t.Id}:{t.Completed}:{t.Text}"));
        if (key != todoKey) { BuildTodo(calculated); todoKey = key; }
        ApplyPanelLock();
        if (Settings.PanelLocked || Settings.ClickThrough) CloseWordLookup();
        else wordLookup?.Refresh();
        UpdateSectionControls();
        Surface.InvalidateVisual(); NativeWindow.Apply(this, Settings);
        if (!HasAddActions || Settings.PanelLocked) AnimateTodoFooter(false, false);
        else if (IsMouseOver) AnimateTodoFooter(true);
        if (transition)
        {
            Surface.TransitionLayout = previous;
            UpdateTodoGeometry();
            StartLayoutTransition(previous!, calculated);
            ResizeWidget(animateScale);
        }
        else { UpdateTodoGeometry(); ResizeWidget(animateScale || !heightChanged); }
    }
    private void ApplyPanelLock()
    {
        var locked = Settings.PanelLocked;
        foreach (var button in toolbar.Children.OfType<Button>())
        {
            button.Visibility = locked && button != lockButton ? Visibility.Collapsed : Visibility.Visible;
            if (locked) HoverHints.Close(button);
        }
        var tip = locked ? "解锁面板" : "锁定面板";
        System.Windows.Automation.AutomationProperties.SetName(lockButton, tip);
        HoverHints.Set(lockButton, tip);
        lockButton.Content = new IconView(locked ? "lock" : "unlock", Surface.Material.Secondary) { Width = 13, Height = 13 };
        Surface.IsHitTestVisible = todoHost.IsHitTestVisible = mediaHost.IsHitTestVisible = !locked;
        if (locked) HoverHints.Close(Surface);
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
            var target = Surface.TargetLayout;
            if (full || GeometryChanged(target)) ApplySettings(animateScale: !full);
            else
            {
                if (LayoutChanged(target))
                {
                    appliedLayout = target;
                    SettingsApplied?.Invoke();
                }
                ResizeWidget(true);
            }
        }));
    }
    private void ResizeWidget(bool animate)
    {
        double target = Math.Clamp(Settings.Scale, .4, 1.2);
        double start = widgetScale.ScaleX; int generation = ++resizeGeneration;
        var geometry = appliedLayout ?? Surface.TargetLayout;
        double width = geometry.Width * target, height = geometry.Height * target;
        void Finish()
        {
            if (generation != resizeGeneration) return;
            widgetScale.ScaleX = widgetScale.ScaleY = target;
            widgetScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); widgetScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            layout.CacheMode = null;
            UpdateTodoGeometry();
            Width = layoutAnimating ? Math.Max(layout.Width, geometry.Width) * target : width;
            Height = footerAnimating || editorAnimating || layoutAnimating
                ? Math.Max(Height, (Surface.Layout.Height + FooterHeight) * target)
                : layout.Height * target;
            if (IsLoaded && geometry.VocabularyRows > 0)
                KeepVocabularyInWorkArea(geometry, target);
            else if (IsLoaded && Top < SystemParameters.WorkArea.Bottom && Top + height > SystemParameters.WorkArea.Bottom)
                Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - height - 12);
        }
        if (!animate || !IsLoaded || !Settings.SmoothResize || Math.Abs(start - target) < .0001) { Finish(); return; }
        // Resize the transparent HWND once to hold both sizes. Animate only the
        // content transform, then shrink the bounds and restore vector rendering.
        double reserveScale = Math.Max(start, target);
        double reserveWidth = layoutAnimating ? Math.Max(layoutFrom!.Width, geometry.Width) : geometry.Width;
        double reserveHeight = layoutAnimating ? Math.Max(layoutFrom!.Height, geometry.Height) + FooterHeight : layout.Height;
        Width = Math.Max(Width, reserveWidth * reserveScale); Height = Math.Max(Height, reserveHeight * reserveScale);
        layout.CacheMode ??= new BitmapCache(1.5);
        var motion = new DoubleAnimation(start, target, TimeSpan.FromMilliseconds(150))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        motion.Completed += (_, _) => Finish();
        widgetScale.BeginAnimation(ScaleTransform.ScaleXProperty, motion, HandoffBehavior.SnapshotAndReplace);
        widgetScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(start, target, motion.Duration)
        { EasingFunction = motion.EasingFunction }, HandoffBehavior.SnapshotAndReplace);
    }
    private void UpdateMediaControls()
    {
        var area = Surface.Layout.Sections.FirstOrDefault(s => s.Kind == WidgetSection.Media);
        var m = State.Media;
        if (area is not null) mediaHost.Margin = new(113 + Surface.SectionInset, area.Top + 62, 0, 0);
        mediaHost.Opacity = area is null ? 0 : area.Opacity * Math.Clamp(area.Height / 104, 0, 1);
        mediaHost.IsHitTestVisible = !Settings.PanelLocked && area is { Height: >= 94, Opacity: >= .9 };
        var key = $"{area is not null}|{m.Playing}|{m.Previous}|{m.Toggle}|{m.Next}|{Settings.Theme}";
        if (key == mediaKey) return; mediaKey = key;
        HoverHints.CloseTree(mediaHost); mediaHost.Children.Clear();
        mediaHost.Visibility = area is null ? Visibility.Collapsed : Visibility.Visible;
        if (area is null) return;
        mediaHost.HorizontalAlignment = HorizontalAlignment.Left; mediaHost.VerticalAlignment = VerticalAlignment.Top;
        mediaHost.Margin = new(113 + Surface.SectionInset, area.Top + 62, 0, 0); mediaHost.Height = 32; mediaHost.Width = 140;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (command, icon, label, enabled) in new[] { ("previous", "previous", "上一首", m.Previous), ("toggle", m.Playing ? "pause" : "play", m.Playing ? "暂停" : "播放", m.Toggle), ("next", "next", "下一首", m.Next) })
        {
            var button = IconButton(icon, label, async () => await Engine.MediaCommandAsync(command));
            button.Width = 44; button.Height = 30; button.IsEnabled = enabled; button.Opacity = enabled ? 1 : .3;
            button.Content = new IconView(icon, Surface.Material.Text, 1.65) { Width = 20, Height = 20 };
            row.Children.Add(button);
        }
        mediaHost.Children.Add(row);
    }
    private void BuildTodo(WidgetLayout calculated)
    {
        var actionsKey = $"{Settings.ShowTodo}|{Settings.ShowVocabulary}";
        if (actionsKey != addActionsKey) { footerMode = ""; addActionsKey = actionsKey; }
        HoverHints.CloseTree(todoHost);
        todoHost.Children.Clear(); todoEditorHost.Children.Clear(); todoEditor = null;
        var area = calculated.Sections.FirstOrDefault(s => s.Kind == WidgetSection.Todo);
        todoHost.Visibility = area is null ? Visibility.Collapsed : Visibility.Visible;
        todoEditorHost.Visibility = HasAddActions ? Visibility.Visible : Visibility.Collapsed;
        if (!HasAddActions) return;
        todoHost.VerticalAlignment = VerticalAlignment.Top; todoHost.HorizontalAlignment = HorizontalAlignment.Left;
        todoHost.Margin = new(30, area?.Top ?? 0, 0, 0); todoHost.Width = 296; todoHost.Height = area?.Height ?? 0;
        todoEditorHost.VerticalAlignment = VerticalAlignment.Top; todoEditorHost.HorizontalAlignment = HorizontalAlignment.Left;
        todoEditorHost.Width = 296; todoEditorHost.Height = 30;
        var material = Surface.Material;
        var rows = new StackPanel();
        foreach (var item in Settings.Todos.Where(t => Settings.ShowTodo && (!Settings.HideCompletedTodos || !t.Completed)))
        {
            var label = new TextBlock { Text = item.Text, TextTrimming = TextTrimming.CharacterEllipsis, Width = 258, FontSize = 13 * Settings.TextScale, FontWeight = FontWeight.FromOpenTypeWeight(Settings.TextWeight),
                Foreground = item.Completed ? material.Quiet : material.Text, TextDecorations = item.Completed ? TextDecorations.Strikethrough : null };
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
        if (area is not null) todoHost.Children.Add(new ScrollViewer { Content = rows, Height = area.Height, VerticalAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        BuildAddEditor();
    }
    private void AnimateEditor(bool expanded)
    {
        double target = expanded ? 1 : 0;
        if (Math.Abs(EditorExpansion - target) < .001 && !editorAnimating) return;
        void Finish() { EditorExpansion = target; BeginAnimation(EditorExpansionProperty, null); editorAnimating = false; UpdateTodoGeometry(); }
        if (!IsLoaded || !Settings.SmoothResize || TodoReveal < .001) { Finish(); return; }
        editorAnimating = true;
        Height = Math.Max(Height, (Surface.Layout.Height + 82) * widgetScale.ScaleY);
        var animation = new DoubleAnimation(EditorExpansion, target, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) => Finish(); BeginAnimation(EditorExpansionProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
    private void BuildAddEditor()
    {
        HoverHints.CloseTree(todoEditorHost); todoEditorHost.Children.Clear();
        bool dark = ThemePalette.Get(Settings.Theme).Dark;
        var material = Surface.Material;
        if (footerMode == "todo" && !Settings.ShowTodo || footerMode == "word" && !Settings.ShowVocabulary) footerMode = "";
        if (footerMode == "" && Settings.ShowTodo && !Settings.ShowVocabulary) footerMode = "todo";
        AnimateEditor(footerMode == "word");
        var content = new Grid();
        todoEditor = new Border { Child = content, Height = footerMode == "word" ? 68 : 30, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new(8), Background = Art.Brush(dark ? "#12FFFFFF" : "#408D9C83") };
        todoEditorHost.Children.Add(todoEditor);
        if (footerMode == "") {
            var actions = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            void Choice(string label, string mode) {
                var button = IconButton("plus", label, () => { footerMode = mode; BuildAddEditor(); });
                button.Width = double.NaN; button.Content = new TextBlock { Text = label, FontSize = 12 * Settings.TextScale, Foreground = material.Secondary }; actions.Children.Add(button);
            }
            if (Settings.ShowTodo) Choice("添加事件", "todo");
            if (Settings.ShowVocabulary) Choice("添加单词", "word");
            content.Children.Add(actions); return;
        }
        content.ColumnDefinitions.Add(new() { Width = new(27) }); content.ColumnDefinitions.Add(new());
        var back = IconButton("close", "返回添加选项", () => { footerMode = ""; BuildAddEditor(); }); content.Children.Add(back);
        var editor = new Grid(); Grid.SetColumn(editor, 1); content.Children.Add(editor);
        if (footerMode == "word") {
            editor.ColumnDefinitions.Add(new()); editor.ColumnDefinitions.Add(new() { Width = new(30) });
            editor.RowDefinitions.Add(new() { Height = new(30) }); editor.RowDefinitions.Add(new() { Height = new(8) }); editor.RowDefinitions.Add(new() { Height = new(30) });
            TextBox Field(string placeholder, int limit, int row) {
                var box = new TextBox { MaxLength = limit, Padding = new(4), Background = Brushes.Transparent, BorderThickness = new(0), Foreground = material.Text, CaretBrush = material.Accent, FontSize = 12 * Settings.TextScale };
                System.Windows.Automation.AutomationProperties.SetName(box, placeholder); HoverHints.Set(box, placeholder);
                var hint = new TextBlock { Text = placeholder, Margin = new(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = material.Secondary, FontSize = 11 * Settings.TextScale, IsHitTestVisible = false };
                var cell = new Grid(); cell.Children.Add(box); cell.Children.Add(hint); Grid.SetRow(cell, row); editor.Children.Add(cell);
                box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                box.GotKeyboardFocus += (_, _) => { todoHideDelay.Stop(); AnimateTodoFooter(true); };
                box.LostKeyboardFocus += (_, _) => { if (!IsMouseOver) SetHover(false); };
                return box;
            }
            var word = Field("单词", 80, 0); var meaning = Field("注释", 200, 2);
            var warning = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            void ClearWarning()
            {
                warning.Stop();
                HoverHints.Close(word);
                HoverHints.Set(word, "单词");
                System.Windows.Automation.AutomationProperties.SetHelpText(word, "");
            }
            warning.Tick += (_, _) => ClearWarning();
            word.TextChanged += (_, _) => { if (warning.IsEnabled) ClearWarning(); };
            word.Unloaded += (_, _) => ClearWarning();
            word.KeyDown += (_, e) => { if (e.Key == Key.Enter) { meaning.Focus(); e.Handled = true; } };
            void AddWord() {
                if (!TryAddVocabulary(word.Text, meaning.Text, out var result))
                {
                    word.Focus();
                    word.SelectAll();
                    HoverHints.Set(word, result);
                    System.Windows.Automation.AutomationProperties.SetHelpText(word, result);
                    if (word.ToolTip is System.Windows.Controls.ToolTip tip) tip.IsOpen = true;
                    warning.Stop();
                    warning.Start();
                    return;
                }
                word.Clear(); meaning.Clear(); ApplySettings(); SaveSettings();
                Dispatcher.BeginInvoke(new Action(() => { if (word.IsVisible && !Settings.PanelLocked) word.Focus(); }));
            }
            var addWord = IconButton("plus", "保存单词", AddWord); Grid.SetColumn(addWord, 1); Grid.SetRow(addWord, 2); editor.Children.Add(addWord);
            meaning.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddWord(); e.Handled = true; } };
            Dispatcher.BeginInvoke(new Action(() => { if (word.IsVisible && !Settings.PanelLocked) word.Focus(); }));
            return;
        }
        var input = new TextBox { Background = Brushes.Transparent, BorderThickness = new(0), Foreground = material.Text, CaretBrush = material.Accent, FontSize = 12 * Settings.TextScale, FontWeight = FontWeight.FromOpenTypeWeight(Settings.TextWeight), Padding = new(5), MaxLength = 160, ToolTip = "输入待办，按 Enter 添加" };
        HoverHints.Set(input, "输入待办，按 Enter 添加");
        var inputRow = new Grid(); inputRow.ColumnDefinitions.Add(new()); inputRow.ColumnDefinitions.Add(new() { Width = new(30) }); inputRow.Children.Add(input);
        var placeholder = new TextBlock { Text = "添加事件", FontSize = 12 * Settings.TextScale, Foreground = material.Secondary, Margin = new(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        inputRow.Children.Add(placeholder);
        input.TextChanged += (_, _) => placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var add = IconButton("plus", "添加待办", () => AddTodo(input)); Grid.SetColumn(add, 1); inputRow.Children.Add(add);
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddTodo(input); e.Handled = true; } };
        editor.Children.Add(inputRow);
        input.GotKeyboardFocus += (_, _) => { todoHideDelay.Stop(); AnimateTodoFooter(true); };
        input.LostKeyboardFocus += (_, _) => { if (!IsMouseOver) SetHover(false); };
    }
    internal bool TryAddVocabulary(string word, string meaning, out string message)
    {
        if (!VocabularyImport.TryAdd(Settings, word, meaning, out message)) return false;
        Surface.RevealLatestVocabulary();
        return true;
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
    internal bool SyncAvailable => persistSettings && !Surface.PreviewMaterial;
    internal static string SyncBackupPath => Path.Combine(SettingsStore.DataDirectory, "mizu-sync-backup.json");
    internal static string CleanupBackupPath => Path.Combine(SettingsStore.DataDirectory, "mizu-cleanup-backup.json");

    internal void SaveFirebaseSync(FirebaseConnection? connection, SyncSnapshot? downloaded = null,
        string? localBackupPath = null, bool pauseAutomaticBackup = false)
    {
        if (!SyncAvailable) throw new InvalidOperationException("预览模式不保存同步设置。");
        store.SaveSyncState(Settings, connection, downloaded, localBackupPath, pauseAutomaticBackup);
        if (downloaded is not null) ApplySettings();
    }

    internal void ReopenSyncSettings(string message)
    {
        OpenSettings();
        settingsWindow?.ShowSync(message);
    }
    public void PersistBeforeUpdate()
    {
        if (!persistSettings) throw new IOException("调试模式不执行更新。");
        store.Save(Settings);
    }
    public void OpenSettings()
    {
        CloseWordLookup();
        Settings.ClickThrough = false; Settings.PanelLocked = false; ApplySettings(); SaveSettings();
        if (settingsWindow is { IsLoaded: true }) { settingsWindow.Activate(); return; }
        settingsWindow = new(this); settingsWindow.Closed += (_, _) => settingsWindow = null; settingsWindow.Show();
    }
    public void OpenHardwareHistory(HardwareMetric? metric = null)
    {
        CloseWordLookup();
        if (historyWindow is { IsLoaded: true }) {
            if (metric is { } chosen) historyWindow.SelectMetric(chosen);
            if (historyWindow.WindowState == WindowState.Minimized) historyWindow.WindowState = WindowState.Normal;
            historyWindow.Activate(); return;
        }
        historyWindow = new(this, metric);
        historyWindow.Closed += (_, _) => historyWindow = null;
        historyWindow.Show();
    }
    internal void NotifyUpdateAvailable(string tag) => tray.ShowBalloonTip(5000, "Mizu 有新版本", tag + " 已可用，点击查看并选择更新。", System.Windows.Forms.ToolTipIcon.Info);
    public async Task RefreshAsync()
    { await Task.WhenAll(Engine.RefreshDevicesAsync(), Engine.RefreshWeatherAsync()); }
    internal void ShowInteractive()
    {
        Settings.ClickThrough = false; ApplySettings(); SaveSettings();
        Show(); EnsureReachablePosition(); Activate();
    }
    public void ToggleVisible() => Dispatcher.Invoke(() => { if (IsVisible) Hide(); else ShowInteractive(); });
    public async Task QuitAsync(int exitCode = 0)
    {
        if (disposed) return; disposed = true; quitting = true;
        Updates.Dispose();
        Backups.Dispose();
        Surface.StopVocabularySpeech();
        settingsWindow?.Close(); historyWindow?.Close(); SaveSettings();
        tray.Visible = false; var trayIcon = tray.Icon; tray.Dispose(); trayIcon?.Dispose();
        NativeWindow.UnregisterHotKey(new WindowInteropHelper(this).Handle, 0x4d5a);
        if (hidNotification != IntPtr.Zero) { NativeWindow.UnregisterDeviceNotification(hidNotification);hidNotification=IntPtr.Zero; }
        todoHideDelay.Stop(); AnimateTodoFooter(false, false);
        displayTimer.Stop();
        Hide(); await Engine.DisposeAsync(); Close(); Application.Current.Shutdown(exitCode);
    }
}
