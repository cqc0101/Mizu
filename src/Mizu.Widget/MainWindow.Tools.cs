using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Mizu.Widget.Controls;

namespace Mizu.Widget;

public sealed partial class MainWindow
{
    private StickyNoteWindow? stickyNoteWindow;
    private ClockWindow? clockWindow;
    private ContextMenu? toolbarMenu;
    private (bool Locked, bool Compact)? toolbarMode;
    private Action? trayNotificationAction;

    private void ApplyToolbarLayout()
    {
        bool compact = (layout.Width - toolbar.Margin.Right) * Settings.Scale < 258;
        var next = (Locked: Settings.PanelLocked, Compact: compact);
        if (toolbarMode == next) return;
        toolbarMode = next;
        CloseToolbarMenu();
        foreach (var button in toolbar.Children.OfType<Button>())
        {
            bool visible = next.Locked ? button == lockButton : button == moreButton ? compact
                : !compact || button != pinButton && button != refreshButton && button != hideButton;
            button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible) HoverHints.Close(button);
        }
    }

    private void ToggleTopmost()
    {
        Settings.AlwaysOnTop = !Settings.AlwaysOnTop;
        ApplySettings();
        SaveSettings();
    }

    private void OpenToolbarMenu()
    {
        if (Settings.PanelLocked || Settings.ClickThrough || moreButton.Visibility != Visibility.Visible) return;
        if (toolbarMenu?.IsOpen == true) { CloseToolbarMenu(); return; }
        CloseWordLookup();
        HoverHints.Close(moreButton);
        var palette = ThemePalette.Get(Settings.Theme);
        var menu = new ContextMenu { PlacementTarget = moreButton, Placement = PlacementMode.Bottom,
            Background = palette.HintBackground, Foreground = Art.Brush(palette.Text), BorderBrush = Art.Brush(palette.Line),
            Padding = new(3), FontFamily = new("Segoe UI, Microsoft YaHei UI") };
        Add("刷新", async () => await RefreshAsync());
        Add(Settings.AlwaysOnTop ? "取消置顶" : "窗口置顶", ToggleTopmost);
        Add("收起到托盘", Hide);
        void Add(string label, Action action)
        {
            var item = new MenuItem { Header = label, Foreground = menu.Foreground, Padding = new(10, 5, 12, 5) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) =>
        {
            if (toolbarMenu != menu) return;
            toolbarMenu = null;
            SetToolbarHover(IsVisible && (IsMouseOver || wordLookup?.IsOpen == true));
        };
        toolbarMenu = menu;
        menu.IsOpen = true;
        SetToolbarHover(true);
    }

    private void CloseToolbarMenu()
    {
        if (toolbarMenu is { IsOpen: true } menu) menu.IsOpen = false;
    }

    private void OpenStickyNote()
    {
        CloseWordLookup();
        CloseToolbarMenu();
        if (stickyNoteWindow is { } existing) { ActivateTool(existing); return; }
        stickyNoteWindow = new(this);
        stickyNoteWindow.Closed += (_, _) => stickyNoteWindow = null;
        stickyNoteWindow.Show();
    }

    private void OpenClockWindow()
    {
        CloseWordLookup();
        CloseToolbarMenu();
        if (clockWindow is { } existing) { ActivateTool(existing); return; }
        clockWindow = new(this);
        clockWindow.Closed += (_, _) => clockWindow = null;
        clockWindow.Show();
        RefreshClock();
    }

    private static void ActivateTool(Window window)
    {
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    internal void RefreshClock()
    {
        var now = DateTimeOffset.Now;
        var reminders = Settings.DesktopClock.CollectReminders(now);
        if (reminders.Count > 0)
        {
            SaveSettings();
            var message = string.Join(" · ", reminders);
            clockWindow?.ShowReminder(message);
            if (persistSettings && !Surface.PreviewMaterial)
            {
                System.Media.SystemSounds.Exclamation.Play();
                if (clockWindow?.IsActive != true) ShowTrayNotification("Mizu 时钟", message, OpenClockWindow);
            }
        }
        clockWindow?.Refresh(now, reminders.Count > 0);
    }

    private void ShowTrayNotification(string title, string message, Action action)
    {
        trayNotificationAction = action;
        tray.ShowBalloonTip(5000, title, message, System.Windows.Forms.ToolTipIcon.Info);
    }
}
