using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using Mizu.Core.Services;
using Mizu.Widget.Controls;

namespace Mizu.Widget;

internal abstract class DesktopToolWindow : Window
{
    protected SettingsTheme Theme { get; }
    protected Grid Body { get; } = new();

    protected DesktopToolWindow(MainWindow owner, string title, double width, double height)
    {
        if (owner.IsVisible) Owner = owner;
        WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        Title = title;
        Icon = AppIcons.WindowIcon;
        ShowInTaskbar = false;
        Width = width;
        Height = height;
        UseLayoutRounding = true;
        FontFamily = new("Segoe UI Variable Display, Segoe UI, Microsoft YaHei UI");
        FontSize = 13;
        Theme = new(this);
        Background = Theme.Brush("#F0F1EC");
        Foreground = Theme.Brush("#41483E");
        foreach (var color in new[] { "#D9E2CF", "#8EA37F", "#8B9F7F", "#DDE2D6", "#FCFDF8", "#8A9F7B", "#5C7650" })
            Theme.Brush(color);
        Body.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Mizu;component/SettingsStyles.xaml", UriKind.Relative) });
        TextElement.SetForeground(Body, Foreground);
        Content = new Border { Child = Body, Padding = new(16), Background = Background };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
        void Retheme() => Theme.Apply(owner.Settings.Theme);
        Retheme();
        SourceInitialized += (_, _) => Retheme();
        owner.SettingsApplied += Retheme;
        Closed += (_, _) => owner.SettingsApplied -= Retheme;
    }

    protected TextBox Field(string label, string text, int maxLength)
    {
        var input = new TextBox { Text = text, MaxLength = maxLength, Padding = new(9, 6, 9, 6),
            Foreground = Foreground, Background = Theme.Brush("#FCFCF8"), BorderBrush = Theme.Brush("#E7EBE1") };
        AutomationProperties.SetName(input, label);
        return input;
    }

    protected Button ToolButton(string text, Action click, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new(13, 7, 13, 7), Cursor = Cursors.Hand,
            Foreground = Foreground, Background = Theme.Brush(primary ? "#DEE6D7" : "#FCFCF8"),
            BorderBrush = Theme.Brush("#E7EBE1") };
        button.Click += (_, _) => click();
        return button;
    }

    protected TextBlock Caption(string text) => new() { Text = text, Foreground = Theme.Brush("#7B8674"), TextWrapping = TextWrapping.Wrap };
}

internal sealed class ClockWindow : DesktopToolWindow
{
    private readonly MainWindow owner;
    private DesktopClock Clock => owner.Settings.DesktopClock;
    private readonly StackPanel alarms = new();
    private readonly StackPanel pages = new();
    private readonly TextBlock time = new() { FontSize = 32, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock reminder;
    private readonly TimerFace countdownFace, focusFace;
    private readonly WrapPanel countdownPresets, focusPresets;
    private readonly TextBlock focusPhase;
    private readonly TextBox countdownMinutes, countdownSeconds, focusMinutes, breakMinutes;
    private readonly Button countdownAction, focusAction;
    private readonly Button[] tabs;
    private readonly FrameworkElement[] sections;
    private string? lastRenderedTime;

    public ClockWindow(MainWindow owner) : base(owner, "Mizu · 时钟", 410, 502)
    {
        this.owner = owner;
        MinWidth = 365;
        MinHeight = 345;
        Body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Body.RowDefinitions.Add(new());
        var heading = new StackPanel { Margin = new(2, 0, 2, 10) };
        Body.Children.Add(heading);
        Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        heading.Children.Add(time);
        reminder = Caption("");
        reminder.Foreground = Theme.Brush("#8EA37F");
        reminder.Margin = new(0, 2, 0, 2);
        reminder.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(reminder, AutomationLiveSetting.Polite);
        heading.Children.Add(reminder);
        var navigation = new UniformGrid { Columns = 3, Margin = new(0, 9, 0, 2) };
        heading.Children.Add(navigation);
        tabs = new[] { "闹钟", "倒计时", "番茄钟" }.Select((title, index) =>
        {
            var button = ToolButton(title, () => SelectPage(index));
            button.Margin = new(0, 0, index == 2 ? 0 : 6, 0);
            navigation.Children.Add(button);
            return button;
        }).ToArray();
        var scroll = new ScrollViewer { Content = pages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        Body.Children.Add(scroll);

        var alarmPage = BuildAlarmPage();
        countdownFace = new(Theme.Brush("#8EA37F"), Theme.Brush("#E7EBE1"));
        focusFace = new(Theme.Brush("#8EA37F"), Theme.Brush("#E7EBE1"));
        countdownMinutes = Number("倒计时分钟", Clock.Countdown.DurationSeconds / 60, 3);
        countdownSeconds = Number("倒计时秒数", Clock.Countdown.DurationSeconds % 60, 2);
        countdownAction = ToolButton("开始", ToggleCountdown, true);
        var countdownPage = TimerPage(null, countdownFace, countdownMinutes, "分钟", countdownSeconds, "秒", countdownAction,
            () => { Clock.Countdown = Clock.Countdown.Reset(); Save(); });
        countdownPresets = AddPresets(countdownPage, new[] { 1, 3, 5, 10, 30 }.Select(minutes =>
            ToolButton($"{minutes} 分钟", () => SelectCountdownPreset(minutes))));
        AddDialToggle(countdownPage, Clock.ShowCountdownDial, enabled => Clock.ShowCountdownDial = enabled);
        focusPhase = Caption("");
        focusPhase.HorizontalAlignment = HorizontalAlignment.Center;
        focusMinutes = Number("专注分钟", Clock.FocusMinutes, 3);
        breakMinutes = Number("休息分钟", Clock.BreakMinutes, 2);
        focusAction = ToolButton("开始专注", ToggleFocus, true);
        var focusPage = TimerPage(focusPhase, focusFace, focusMinutes, "专注分钟", breakMinutes, "休息分钟", focusAction,
            () => { Clock.OnBreak = false; Clock.Focus = new(Clock.FocusMinutes * 60); Save(); });
        focusPresets = AddPresets(focusPage, new[] { (Focus: 25, Break: 5), (Focus: 50, Break: 10), (Focus: 90, Break: 15) }.Select(preset =>
            ToolButton($"{preset.Focus} / {preset.Break} 分钟", () => SelectFocusPreset(preset.Focus, preset.Break))));
        AddDialToggle(focusPage, Clock.ShowFocusDial, enabled => Clock.ShowFocusDial = enabled);
        sections = [alarmPage, countdownPage, focusPage];
        foreach (var section in sections) pages.Children.Add(section);
        SelectPage(0);
        Refresh(DateTimeOffset.Now, true);
    }

    private void SelectPage(int index)
    {
        for (int i = 0; i < sections.Length; i++)
        {
            sections[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            tabs[i].Background = Theme.Brush(i == index ? "#DEE6D7" : "#FCFCF8");
        }
    }

    private StackPanel BuildAlarmPage()
    {
        var panel = new StackPanel();
        var input = Field("闹钟时间", DateTime.Now.AddMinutes(1).ToString("HH:mm", CultureInfo.InvariantCulture), 5);
        var name = Field("闹钟备注", "", 48);
        input.Width = 94;
        input.Padding = new(5, 6, 5, 6);
        input.TextAlignment = TextAlignment.Center;
        var daily = new CheckBox { Content = "每天重复", Foreground = Foreground, Margin = new(2, 8, 0, 2) };
        var fields = new DockPanel { Margin = new(0, 5, 0, 0) };
        DockPanel.SetDock(input, Dock.Left);
        fields.Children.Add(input);
        name.Margin = new(7, 0, 0, 0);
        fields.Children.Add(name);
        panel.Children.Add(Caption("时间（24 小时制）· 备注可选"));
        panel.Children.Add(fields);
        var actions = new DockPanel { Margin = new(0, 4, 0, 10), LastChildFill = false };
        actions.Children.Add(daily);
        var add = ToolButton("添加闹钟", () =>
        {
            if (!TimeOnly.TryParseExact(input.Text.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var when))
            { ShowReminder("请输入有效的时间，例如 09:30。"); input.Focus(); return; }
            Clock.Alarms.Add(new(Guid.NewGuid(), when, name.Text.Trim(), daily.IsChecked == true, DesktopClock.NextAlarm(when, DateTimeOffset.Now)));
            name.Clear();
            Save(true);
        }, true);
        DockPanel.SetDock(add, Dock.Right);
        actions.Children.Add(add);
        panel.Children.Add(actions);
        panel.Children.Add(alarms);
        return panel;
    }

    private void RenderAlarms()
    {
        alarms.Children.Clear();
        if (Clock.Alarms.Count == 0) { alarms.Children.Add(Caption("暂无闹钟")); return; }
        foreach (var alarm in Clock.Alarms.OrderBy(alarm => alarm.Time))
        {
            var row = new DockPanel { Margin = new(0, 0, 0, 6) };
            var remove = ToolButton("删除", () => { Clock.Alarms.RemoveAll(item => item.Id == alarm.Id); Save(true); });
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(remove);
            var toggle = ToolButton(alarm.DueAt is null ? "开启" : "关闭", () =>
            {
                var index = Clock.Alarms.FindIndex(item => item.Id == alarm.Id);
                if (index < 0) return;
                var current = Clock.Alarms[index];
                Clock.Alarms[index] = current with { DueAt = current.DueAt is null ? DesktopClock.NextAlarm(current.Time, DateTimeOffset.Now) : null };
                Save(true);
            });
            toggle.Margin = new(3, 0, 6, 0);
            DockPanel.SetDock(toggle, Dock.Right);
            row.Children.Add(toggle);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text = alarm.Time.ToString("HH:mm", CultureInfo.InvariantCulture), FontSize = 17 });
            labels.Children.Add(Caption((alarm.Daily ? "每天" : "单次") + (string.IsNullOrEmpty(alarm.Label) ? "" : " · " + alarm.Label)));
            row.Children.Add(labels);
            alarms.Children.Add(row);
        }
    }

    private StackPanel TimerPage(TextBlock? phase, FrameworkElement display, TextBox first, string firstLabel, TextBox second,
        string secondLabel, Button action, Action reset)
    {
        var panel = new StackPanel { Margin = new(0, 4, 0, 4) };
        if (phase is not null) panel.Children.Add(phase);
        panel.Children.Add(display);
        var fields = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 6, 0, 11) };
        AddField(first, firstLabel);
        AddField(second, secondLabel);
        void AddField(TextBox box, string label)
        {
            box.Margin = new(6, 0, 4, 0);
            fields.Children.Add(box);
            var caption = Caption(label);
            caption.VerticalAlignment = VerticalAlignment.Center;
            fields.Children.Add(caption);
        }
        panel.Children.Add(fields);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        buttons.Children.Add(action);
        var clear = ToolButton("重置", reset);
        clear.Margin = new(7, 0, 0, 0);
        buttons.Children.Add(clear);
        panel.Children.Add(buttons);
        return panel;
    }

    private static WrapPanel AddPresets(Panel page, IEnumerable<Button> buttons)
    {
        var tray = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 8, 0, 0) };
        foreach (var button in buttons)
        {
            button.Padding = new(7, 5, 7, 5);
            button.Margin = new(2);
            tray.Children.Add(button);
        }
        page.Children.Add(tray);
        return tray;
    }

    private void AddDialToggle(Panel page, bool enabled, Action<bool> apply)
    {
        var toggle = new CheckBox { Content = "圆盘走时", IsChecked = enabled, HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = Foreground, Margin = new(0, 9, 0, 4) };
        toggle.Click += (_, _) => { apply(toggle.IsChecked == true); Save(); };
        page.Children.Add(toggle);
    }

    private void SelectCountdownPreset(int minutes)
    {
        countdownMinutes.Text = minutes.ToString(CultureInfo.InvariantCulture);
        countdownSeconds.Text = "0";
        Clock.Countdown = new(minutes * 60);
        Save();
    }

    private void SelectFocusPreset(int focus, int rest)
    {
        focusMinutes.Text = focus.ToString(CultureInfo.InvariantCulture);
        breakMinutes.Text = rest.ToString(CultureInfo.InvariantCulture);
        Clock.FocusMinutes = focus;
        Clock.BreakMinutes = rest;
        Clock.Focus = new((Clock.OnBreak ? rest : focus) * 60);
        Save();
    }

    private TextBox Number(string label, int initial, int maxLength)
    {
        var input = Field(label, initial.ToString(CultureInfo.InvariantCulture), maxLength);
        input.Width = 62;
        input.Padding = new(4, 6, 4, 6);
        input.TextAlignment = TextAlignment.Center;
        return input;
    }

    private static bool ReadNumber(TextBox input, int min, int max, out int value) =>
        int.TryParse(input.Text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    private void ToggleCountdown()
    {
        var running = Clock.Countdown.DueAt is not null;
        owner.RefreshClock();
        var now = DateTimeOffset.Now;
        var timer = Clock.Countdown;
        if (running && timer.DueAt is null) return;
        if (timer.DueAt is not null) Clock.Countdown = timer.Pause(now);
        else if (timer.PausedSeconds is not null) Clock.Countdown = timer.Start(now);
        else
        {
            if (!ReadNumber(countdownMinutes, 0, 999, out var minutes) ||
                !ReadNumber(countdownSeconds, 0, 59, out var seconds) || minutes + seconds == 0)
            { ShowReminder("请输入有效的分钟和秒数。"); return; }
            Clock.Countdown = new DesktopTimer(minutes * 60 + seconds).Start(now);
        }
        Save();
    }

    private void ToggleFocus()
    {
        var running = Clock.Focus.DueAt is not null;
        owner.RefreshClock();
        var now = DateTimeOffset.Now;
        var timer = Clock.Focus;
        if (running && timer.DueAt is null) return;
        if (timer.DueAt is not null) Clock.Focus = timer.Pause(now);
        else if (timer.PausedSeconds is not null) Clock.Focus = timer.Start(now);
        else
        {
            if (!ReadNumber(focusMinutes, 1, 180, out var focus) || !ReadNumber(breakMinutes, 1, 60, out var rest))
            { ShowReminder("专注可设 1–180 分钟，休息可设 1–60 分钟。"); return; }
            Clock.FocusMinutes = focus;
            Clock.BreakMinutes = rest;
            Clock.Focus = new DesktopTimer((Clock.OnBreak ? rest : focus) * 60).Start(now);
        }
        Save();
    }

    private void Save(bool refreshAlarms = false)
    {
        reminder.Visibility = Visibility.Collapsed;
        owner.SaveSettings();
        Refresh(DateTimeOffset.Now, refreshAlarms);
    }

    public void ShowReminder(string message)
    {
        reminder.Text = message;
        reminder.Visibility = Visibility.Visible;
    }

    public void Refresh(DateTimeOffset now, bool refreshAlarms)
    {
        var current = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (lastRenderedTime != current) { time.Text = current; lastRenderedTime = current; }
        if (refreshAlarms) RenderAlarms();
        countdownFace.Update(Clock.Countdown, now, Clock.ShowCountdownDial);
        focusFace.Update(Clock.Focus, now, Clock.ShowFocusDial);
        countdownAction.Content = ActionLabel(Clock.Countdown, "开始");
        focusAction.Content = ActionLabel(Clock.Focus, Clock.OnBreak ? "开始休息" : "开始专注");
        countdownPresets.IsEnabled = countdownMinutes.IsEnabled = countdownSeconds.IsEnabled = Clock.Countdown is { DueAt: null, PausedSeconds: null };
        focusPresets.IsEnabled = focusMinutes.IsEnabled = breakMinutes.IsEnabled = Clock.Focus is { DueAt: null, PausedSeconds: null };
        focusPhase.Text = Clock.OnBreak ? "休息" : "专注";
    }

    private static string ActionLabel(DesktopTimer timer, string startText) => timer.DueAt is not null ? "暂停" :
        timer.PausedSeconds is not null ? "继续" : startText;
}
