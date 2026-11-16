using Mizu.Core.Services;

namespace Mizu.Core;

public enum WidgetSection { Weather, Devices, Metrics, Network, Media, Poetry, Todo, Vocabulary, Clock }
public sealed record SectionLayout(WidgetSection Kind, double Top, double Height);
public sealed record WidgetLayout(SectionLayout[] Sections, double Height)
{
    public static double VocabularyRowHeight(AppSettings s) => (s.VocabularyHideMeaning ? 34 : 58) + s.VocabularySpacing;
    public static WidgetLayout Calculate(AppSettings s, int deviceCount = 2)
    {
        var rows = new List<SectionLayout>(); double y = 32;
        void Add(WidgetSection kind, double height) { if (height <= 0) return; if (rows.Count > 0) y += 16; rows.Add(new(kind, y, height)); y += height; }
        if (s.ShowClock) Add(WidgetSection.Clock, 94);
        if (s.ShowWeather)
        {
            double h = s.WeatherCurrent || s.WeatherTodayRange ? (s.CompactMode ? 84 : 116) : 0;
            if (s.WeatherHourly) h += Math.Ceiling(s.ForecastHours / 5.0) * 60;
            if (s.WeatherDaily) h += Math.Ceiling(s.ForecastDays / 4.0) * 70;
            Add(WidgetSection.Weather, h);
        }
        if (s.ShowDevices) Add(WidgetSection.Devices, Math.Clamp(deviceCount, 0, 2) * (s.CompactMode ? 56 : 78));
        if (s.ShowCpu || s.ShowMemory || s.ShowTemperature) Add(WidgetSection.Metrics, s.CompactMode || !s.ShowGraphs ? 52 : 84);
        if (s.ShowNetwork) Add(WidgetSection.Network, 60 + (s.NetworkProbeTargets?.Count ?? 1) * (s.ShowNetworkProbeIp ? 52 : 32));
        if (s.ShowMedia) Add(WidgetSection.Media, 104);
        if (s.ShowPoetry) Add(WidgetSection.Poetry, 64);
        if (s.ShowVocabulary && s.Vocabulary.Count > 0) Add(WidgetSection.Vocabulary, Math.Min(s.Vocabulary.Count, s.VocabularyVisibleRows) * VocabularyRowHeight(s));
        if (s.ShowTodo)
        {
            var count = s.Todos.Count(t => !s.HideCompletedTodos || !t.Completed);
            if (count > 0) Add(WidgetSection.Todo, Math.Min(count, s.TodoVisibleRows) * 32);
        }
        var order = s.SectionOrder;
        var sorted = rows.OrderBy(row => { var index = order.IndexOf(row.Kind); return index < 0 ? int.MaxValue : index; }).ToArray();
        y = 32;
        for (int i = 0; i < sorted.Length; i++) { if (i > 0) y += 16; sorted[i] = sorted[i] with { Top = y }; y += sorted[i].Height; }
        return new(sorted, Math.Max(104, y + 20));
    }
}
