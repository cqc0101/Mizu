using Mizu.Core.Services;

namespace Mizu.Core;

public enum WidgetSection { Weather, Devices, Metrics, Network, Media, Poetry, Todo, Vocabulary, Clock, GpuMemory }
public sealed record SectionLayout(WidgetSection Kind, double Top, double Height)
{
    public double Opacity { get; init; } = 1;
}
public sealed record WidgetLayout(SectionLayout[] Sections, double Height)
{
    public static double VocabularyRowHeight(AppSettings s) => (s.VocabularyHideMeaning ? 34 : 58) + s.VocabularySpacing;
    public static WidgetLayout Interpolate(WidgetLayout from, WidgetLayout to, double progress)
    {
        double height = Mix(from.Height, to.Height, progress);
        var oldOrder = from.Sections.Select(s => s.Kind).ToList();
        var newOrder = to.Sections.Select(s => s.Kind).ToList();
        if (!oldOrder.Where(newOrder.Contains).SequenceEqual(newOrder.Where(oldOrder.Contains)))
        {
            // Switching order at zero opacity keeps the panel's rows from passing through each other.
            var phase = progress < .5 ? from : to;
            double opacity = Math.Abs(progress * 2 - 1);
            return new(phase.Sections.Select(s => s with { Opacity = s.Opacity * opacity }).ToArray(), height);
        }

        // Insert each new row immediately before the next shared row. Rows missing
        // at either endpoint start or end collapsed at that slot.
        var order = new List<WidgetSection>(oldOrder);
        for (int i = 0; i < newOrder.Count; i++)
        {
            if (order.Contains(newOrder[i])) continue;
            int insertAt = order.Count;
            for (int next = i + 1; next < newOrder.Count; next++)
            {
                int existing = order.IndexOf(newOrder[next]);
                if (existing < 0) continue;
                insertAt = existing;
                break;
            }
            order.Insert(insertAt, newOrder[i]);
        }
        SectionLayout At(WidgetLayout layout, int index)
        {
            var kind = order[index];
            var section = layout.Sections.FirstOrDefault(s => s.Kind == kind);
            if (section is not null) return section;
            for (int next = index + 1; next < order.Count; next++)
            {
                var following = layout.Sections.FirstOrDefault(s => s.Kind == order[next]);
                if (following is not null) return new(kind, following.Top, 0) { Opacity = 0 };
            }
            var last = layout.Sections.LastOrDefault();
            return new(kind, last is null ? 32 : last.Top + last.Height, 0) { Opacity = 0 };
        }
        var sections = new SectionLayout[order.Count];
        for (int i = 0; i < order.Count; i++)
        {
            var old = At(from, i);
            var next = At(to, i);
            sections[i] = new(order[i], Mix(old.Top, next.Top, progress), Mix(old.Height, next.Height, progress))
            {
                Opacity = Mix(old.Opacity, next.Opacity, progress)
            };
        }
        return new(sections, height);
    }
    private static double Mix(double from, double to, double progress) => from + (to - from) * progress;
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
        if (s.ShowGpuMemory) Add(WidgetSection.GpuMemory, 56);
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
