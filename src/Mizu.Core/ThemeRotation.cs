using Mizu.Core.Services;
namespace Mizu.Core;

public sealed class ThemeRotation
{
    private bool enabled;
    private int minutes;
    private string? lastTheme;
    private DateTimeOffset deadline;
    public bool Update(AppSettings settings, DateTimeOffset now, IEnumerable<string> themes)
    {
        if (enabled != settings.RandomThemeEnabled || minutes != settings.ThemeIntervalMinutes || lastTheme != settings.Theme) deadline = now.AddMinutes(settings.ThemeIntervalMinutes);
        enabled = settings.RandomThemeEnabled; minutes = settings.ThemeIntervalMinutes; lastTheme = settings.Theme;
        if (!enabled || now < deadline) return false;
        deadline = now.AddMinutes(minutes);
        var choices = themes.Where(t => t != settings.Theme).Distinct().ToArray();
        if (choices.Length == 0) return false;
        settings.Theme = choices[Random.Shared.Next(choices.Length)]; lastTheme = settings.Theme; return true;
    }
}
