using Mizu.Core.Services;

namespace Mizu.Core;

public enum WidgetSection { Weather, Devices, Metrics, Network, Media, Poetry, Todo }
public sealed record SectionLayout(WidgetSection Kind, double Top, double Height);
public sealed record WidgetLayout(SectionLayout[] Sections, double Height)
{
    public static WidgetLayout Calculate(AppSettings s)
    {
        var rows = new List<SectionLayout>(); double y = 32;
        void Add(WidgetSection kind, double height) { if (height <= 0) return; if (rows.Count > 0) y += 16; rows.Add(new(kind, y, height)); y += height; }
        if (s.ShowWeather)
        {
            double h = s.WeatherCurrent || s.WeatherTodayRange ? (s.CompactMode ? 84 : 116) : 0;
            if (s.WeatherHourly) h += Math.Ceiling(s.ForecastHours / 5.0) * 60;
            if (s.WeatherDaily) h += Math.Ceiling(s.ForecastDays / 4.0) * 70;
            Add(WidgetSection.Weather, h);
        }
        if (s.ShowDevices) Add(WidgetSection.Devices, s.CompactMode ? 112 : 156);
        if (s.ShowCpu || s.ShowMemory || s.ShowTemperature) Add(WidgetSection.Metrics, s.CompactMode || !s.ShowGraphs ? 52 : 84);
        if (s.ShowNetwork) Add(WidgetSection.Network, 44 + (s.NetworkProbeTargets?.Count ?? 1) * (s.ShowNetworkProbeIp ? 44 : 28));
        if (s.ShowMedia) Add(WidgetSection.Media, 104);
        if (s.ShowPoetry) Add(WidgetSection.Poetry, 64);
        if (s.ShowTodo)
        {
            var count = s.Todos.Count(t => !s.HideCompletedTodos || !t.Completed);
            if (count > 0) Add(WidgetSection.Todo, Math.Min(count, s.TodoVisibleRows) * 32);
        }
        return new(rows.ToArray(), Math.Max(104, y + 20));
    }
}
