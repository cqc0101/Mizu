using Mizu.Core.Services;

namespace Mizu.Core;

public sealed class VocabularyRotation
{
    private VocabularyEntry[] entries = [];
    private VocabularyEntry[] order = [];
    private int size = 1, columns = 1, minutes;
    private bool random, enabled;
    private DateTimeOffset deadline;
    public IReadOnlyList<VocabularyEntry> Order => order;
    public int TotalRowCount => (order.Length + columns - 1) / columns;
    public int VisibleRowCount => Math.Min(size, TotalRowCount);
    public int MaximumOffset => TotalRowCount - VisibleRowCount;
    public double Offset { get; private set; }

    public void Update(AppSettings settings, DateTimeOffset now, int rowLimit = WidgetLayout.MaximumVocabularyRows, int columnCount = 1)
    {
        ApplySettings(settings, now, rowLimit, columnCount);
        if (!enabled || now < deadline) return;

        if (Offset < MaximumOffset) Offset = Math.Min(Offset + VisibleRowCount, MaximumOffset);
        else RestartCycle();
        deadline = now.AddMinutes(minutes);
    }

    public void Scroll(AppSettings settings, int wheelDelta, double displayedOffset, DateTimeOffset now, int rowLimit = WidgetLayout.MaximumVocabularyRows, int columnCount = 1)
    {
        var previousOrder = order;
        int previousColumns = columns;
        ApplySettings(settings, now, rowLimit, columnCount);
        if (!enabled || wheelDelta == 0) return;

        deadline = now.AddMinutes(minutes);
        double movement = -wheelDelta / 120d;
        double displayed = ReferenceEquals(previousOrder, order)
            ? Math.Clamp(displayedOffset * previousColumns / columns, 0, MaximumOffset) : 0;
        // Discard unfinished travel in the opposite direction as soon as the user reverses.
        double start = (Offset - displayed) * movement < 0 ? displayed : Offset;
        Offset = Math.Clamp(start + movement, 0, MaximumOffset);
    }

    private void ApplySettings(AppSettings settings, DateTimeOffset now, int rowLimit, int columnCount)
    {
        int pageSize = Math.Clamp(settings.VocabularyVisibleRows, 1, rowLimit);
        bool orderChanged = random != settings.VocabularyRandom || !entries.SequenceEqual(settings.Vocabulary);
        bool sizeChanged = size != pageSize || columns != columnCount;
        int firstEntry = (int)Offset * columns;
        size = pageSize;
        int previousColumns = columns;
        columns = columnCount;
        if (orderChanged)
        {
            entries = settings.Vocabulary.ToArray();
            random = settings.VocabularyRandom;
            RestartCycle();
        }
        else if (sizeChanged) Offset = Math.Min(previousColumns == columns ? Offset : firstEntry / columns, MaximumOffset);
        if (orderChanged || sizeChanged || minutes != settings.VocabularyMinutes || enabled != settings.ShowVocabulary)
            deadline = now.AddMinutes(settings.VocabularyMinutes);
        minutes = settings.VocabularyMinutes;
        enabled = settings.ShowVocabulary;
    }

    private void RestartCycle()
    {
        var previousOrder = order;
        int first = Math.Min((int)Offset * columns, Math.Max(0, previousOrder.Length - 1));
        Offset = 0;
        if (!random)
        {
            order = entries;
            return;
        }
        order = entries.ToArray();
        Random.Shared.Shuffle(order);
        int count = Math.Min(order.Length, VisibleRowCount * columns);
        if (order.Length > 1 && previousOrder.Length - first >= count &&
            order.AsSpan(0, count).SequenceEqual(previousOrder.AsSpan(first, count)))
            (order[0], order[^1]) = (order[^1], order[0]);
    }
}
