using Mizu.Core.Services;

namespace Mizu.Core;

public sealed class VocabularyRotation
{
    private VocabularyEntry[] entries = [];
    private VocabularyEntry[] order = [];
    private int size = 1, minutes;
    private bool random, enabled;
    private DateTimeOffset deadline;
    public IReadOnlyList<VocabularyEntry> Order => order;
    public int VisibleRowCount => Math.Min(size, order.Length);
    public int MaximumOffset => order.Length - VisibleRowCount;
    public double Offset { get; private set; }

    public void Update(AppSettings settings, DateTimeOffset now)
    {
        ApplySettings(settings, now);
        if (!enabled || now < deadline) return;

        if (Offset < MaximumOffset) Offset = Math.Min(Offset + VisibleRowCount, MaximumOffset);
        else RestartCycle();
        deadline = now.AddMinutes(minutes);
    }

    public void Scroll(AppSettings settings, int wheelDelta, double displayedOffset, DateTimeOffset now)
    {
        var previousOrder = order;
        ApplySettings(settings, now);
        if (!enabled || wheelDelta == 0) return;

        deadline = now.AddMinutes(minutes);
        double movement = -wheelDelta / 120d;
        double displayed = ReferenceEquals(previousOrder, order) ? Math.Clamp(displayedOffset, 0, MaximumOffset) : 0;
        // Discard unfinished travel in the opposite direction as soon as the user reverses.
        double start = (Offset - displayed) * movement < 0 ? displayed : Offset;
        Offset = Math.Clamp(start + movement, 0, MaximumOffset);
    }

    private void ApplySettings(AppSettings settings, DateTimeOffset now)
    {
        int pageSize = Math.Clamp(settings.VocabularyVisibleRows, 1, 12);
        bool changed = size != pageSize || random != settings.VocabularyRandom || !entries.SequenceEqual(settings.Vocabulary);
        if (changed)
        {
            entries = settings.Vocabulary.ToArray();
            size = pageSize;
            random = settings.VocabularyRandom;
            RestartCycle();
        }
        if (changed || minutes != settings.VocabularyMinutes || enabled != settings.ShowVocabulary)
            deadline = now.AddMinutes(settings.VocabularyMinutes);
        minutes = settings.VocabularyMinutes;
        enabled = settings.ShowVocabulary;
    }

    private void RestartCycle()
    {
        var previousOrder = order;
        int first = Math.Min((int)Offset, Math.Max(0, previousOrder.Length - size));
        Offset = 0;
        if (!random)
        {
            order = entries;
            return;
        }
        order = entries.ToArray();
        Random.Shared.Shuffle(order);
        int count = VisibleRowCount;
        if (order.Length > 1 && previousOrder.Length - first >= count &&
            order.AsSpan(0, count).SequenceEqual(previousOrder.AsSpan(first, count)))
            (order[0], order[^1]) = (order[^1], order[0]);
    }
}
