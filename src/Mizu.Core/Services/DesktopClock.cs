namespace Mizu.Core.Services;

public sealed record DesktopAlarm(Guid Id, TimeOnly Time, string Label, bool Daily, DateTimeOffset? DueAt);

public sealed record DesktopTimer(int DurationSeconds, DateTimeOffset? DueAt = null, int? PausedSeconds = null, bool Finished = false)
{
    public int Remaining(DateTimeOffset now) => DueAt is { } due
        ? (int)Math.Clamp(Math.Ceiling((due - now).TotalSeconds), 0, DurationSeconds)
        : Finished ? 0 : PausedSeconds ?? DurationSeconds;

    public DesktopTimer Start(DateTimeOffset now) => this with
    {
        DueAt = now.AddSeconds(PausedSeconds ?? DurationSeconds),
        PausedSeconds = null,
        Finished = false
    };

    public DesktopTimer Pause(DateTimeOffset now) => this with { DueAt = null, PausedSeconds = Remaining(now) };
    public DesktopTimer Reset() => new(DurationSeconds);

    internal bool IsDue(DateTimeOffset now) => DueAt is { } due && due <= now;

    internal DesktopTimer Normalize(int fallback) => DurationSeconds is > 0 and <= 59_999
        ? this with { PausedSeconds = DueAt is null && PausedSeconds is { } paused ? Math.Clamp(paused, 1, DurationSeconds) : null }
        : new(fallback);
}

public sealed class DesktopClock
{
    public List<DesktopAlarm> Alarms { get; set; } = [];
    public DesktopTimer Countdown { get; set; } = new(300);
    public DesktopTimer Focus { get; set; } = new(1500);
    public int FocusMinutes { get; set; } = 25;
    public int BreakMinutes { get; set; } = 5;
    public bool OnBreak { get; set; }

    public static DateTimeOffset NextAlarm(TimeOnly time, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        DateTime localDate = TimeZoneInfo.ConvertTime(now, zone).Date;
        DateTimeOffset OnDay(DateTime date)
        {
            DateTime candidate = date.Add(time.ToTimeSpan());
            while (zone.IsInvalidTime(candidate)) candidate = candidate.AddMinutes(1);
            var offsets = zone.IsAmbiguousTime(candidate) ? zone.GetAmbiguousTimeOffsets(candidate) : [zone.GetUtcOffset(candidate)];
            return offsets.Select(offset => new DateTimeOffset(candidate, offset)).Order().First();
        }
        var today = OnDay(localDate);
        return today > now ? today : OnDay(localDate.AddDays(1));
    }

    public IReadOnlyList<string> CollectReminders(DateTimeOffset now)
    {
        List<string>? reminders = null;
        for (int i = 0; i < Alarms.Count; i++)
        {
            var alarm = Alarms[i];
            if (alarm.DueAt is not { } due || due > now) continue;
            (reminders ??= []).Add(string.IsNullOrWhiteSpace(alarm.Label) ? $"闹钟 {alarm.Time:HH:mm}" : alarm.Label);
            Alarms[i] = alarm with { DueAt = alarm.Daily ? NextAlarm(alarm.Time, now) : null };
        }
        if (Countdown.IsDue(now))
        {
            (reminders ??= []).Add("倒计时结束");
            Countdown = new(Countdown.DurationSeconds, Finished: true);
        }
        if (Focus.IsDue(now))
        {
            (reminders ??= []).Add(OnBreak ? "休息结束" : "专注结束");
            OnBreak = !OnBreak;
            Focus = new((OnBreak ? BreakMinutes : FocusMinutes) * 60);
        }
        return (IReadOnlyList<string>?)reminders ?? Array.Empty<string>();
    }

    public void Normalize()
    {
        Alarms = (Alarms ?? []).Where(alarm => alarm is not null).ToList();
        Countdown = (Countdown ?? new(300)).Normalize(300);
        FocusMinutes = Math.Clamp(FocusMinutes, 1, 180);
        BreakMinutes = Math.Clamp(BreakMinutes, 1, 60);
        var seconds = (OnBreak ? BreakMinutes : FocusMinutes) * 60;
        Focus = (Focus ?? new(seconds)).Normalize(seconds);
    }
}
