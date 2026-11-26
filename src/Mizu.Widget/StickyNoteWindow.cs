using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mizu.Core.Services;
using Mizu.Widget.Controls;

namespace Mizu.Widget;

internal sealed class StickyNoteWindow : Window
{
    public StickyNoteWindow(MainWindow widget)
    {
        Title = "Mizu · 便签";
        Icon = AppIcons.WindowIcon;
        Width = 326;
        Height = 250;
        MinWidth = 240;
        MinHeight = 160;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Topmost = widget.Settings.StickyNoteTopmost;
        UseLayoutRounding = true;
        FontFamily = new("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI");
        var material = widget.Surface.Material;
        var face = new GlassFace(widget);
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = new(46) });
        root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new() { Height = new(23) });
        root.Children.Add(face);
        Grid.SetRowSpan(face, 3);
        Content = root;

        var heading = new DockPanel { Background = Brushes.Transparent, Margin = new(18, 8, 16, 3) };
        heading.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left || e.ClickCount != 1) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        };
        root.Children.Add(heading);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(actions, Dock.Right);
        heading.Children.Add(actions);
        heading.Children.Add(new TextBlock { Text = "便签", FontSize = 13, FontWeight = FontWeights.SemiBold,
            Foreground = material.Secondary, VerticalAlignment = VerticalAlignment.Center });
        var pin = widget.IconButton("pin", "便签置顶");
        pin.Click += (_, _) =>
        {
            Topmost = !Topmost;
            widget.Settings.StickyNoteTopmost = Topmost;
            RefreshPin();
            widget.SaveSettings();
        };
        void RefreshPin()
        {
            var label = Topmost ? "取消便签置顶" : "便签置顶";
            pin.Content = new IconView("pin", Topmost ? material.Accent : material.Secondary,
                Topmost ? 1.7 : 1.2) { Width = 13, Height = 13 };
            HoverHints.Set(pin, label);
            AutomationProperties.SetName(pin, label);
        }
        RefreshPin();
        actions.Children.Add(pin);
        actions.Children.Add(widget.IconButton("minimize", "最小化便签", () => WindowState = WindowState.Minimized));
        actions.Children.Add(widget.IconButton("close", "关闭便签", Close));

        var note = new TextBox { Text = widget.Settings.StickyNote, MaxLength = AppSettings.MaximumStickyNoteLength,
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new(18, 0, 16, 0), Padding = new(0, 4, 3, 0), FontSize = 14, Background = Brushes.Transparent,
            BorderThickness = new(0), FocusVisualStyle = null, Foreground = material.Text, CaretBrush = material.Accent,
            SelectionBrush = material.Accent };
        AutomationProperties.SetName(note, "便签内容");
        Grid.SetRow(note, 1);
        root.Children.Add(note);

        var grip = new Thumb { Width = 23, Height = 20, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 0, 7, 0), Cursor = Cursors.SizeNWSE,
            Focusable = false, Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
                """<ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Thumb"><Border Background="Transparent"/></ControlTemplate>""") };
        grip.DragDelta += (_, e) =>
        {
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
            Height = Math.Max(MinHeight, Height + e.VerticalChange);
        };
        var gripMark = new IconView("resize", material.Secondary) { Width = 10, Height = 10, Margin = new(0, 2, 13, 0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        Grid.SetRow(grip, 2); Grid.SetRow(gripMark, 2);
        root.Children.Add(grip); root.Children.Add(gripMark);

        void RefreshMaterial()
        {
            _ = widget.Surface.Material;
            var radius = Math.Min(widget.Settings.CornerRadius, Math.Min(root.ActualWidth, root.ActualHeight) / 2);
            root.Clip = new RectangleGeometry(new Rect(0, 0, root.ActualWidth, root.ActualHeight), radius, radius);
            var headingInset = Math.Max(18, radius * .64);
            heading.Margin = new(headingInset, 8, headingInset, 3);
            var gripInset = Math.Max(7, radius * .42);
            grip.Margin = new(0, 0, gripInset, 0);
            gripMark.Margin = new(0, 2, gripInset + 6, 0);
            HoverHints.SetPalette(this, ThemePalette.Get(widget.Settings.Theme));
            HoverHints.SetIsDark(this, ThemePalette.Get(widget.Settings.Theme).Dark);
            face.InvalidateVisual();
        }
        root.SizeChanged += (_, _) => RefreshMaterial();
        widget.SettingsApplied += RefreshMaterial;
        Closed += (_, _) => widget.SettingsApplied -= RefreshMaterial;

        var save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        void Save()
        {
            save.Stop();
            widget.SaveSettings();
        }
        save.Tick += (_, _) => Save();
        note.TextChanged += (_, _) =>
        {
            widget.Settings.StickyNote = note.Text;
            save.Stop();
            save.Start();
        };
        Closing += (_, _) => { if (save.IsEnabled) Save(); };
        Loaded += (_, _) => { note.Focus(); note.CaretIndex = note.Text.Length; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    private sealed class GlassFace(MainWindow widget) : FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing)
        {
            if (ActualWidth <= 2 || ActualHeight <= 2) return;
            var radius = Math.Min(widget.Settings.CornerRadius, Math.Min(ActualWidth, ActualHeight) / 2);
            widget.Surface.Material.Draw(drawing, new Rect(0, 0, ActualWidth, ActualHeight), radius);
        }
    }
}
