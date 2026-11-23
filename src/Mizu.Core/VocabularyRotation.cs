using Mizu.Core.Services;

namespace Mizu.Core;

public sealed class VocabularyRotation
{
    private VocabularyEntry[] entries = [];
    private VocabularyEntry[] order = [];
    private VocabularyEntry[] visible = [];
    private int page, size = 1, minutes;
    private bool random, enabled;
    private DateTimeOffset deadline;
    public IReadOnlyList<VocabularyEntry> Visible => visible;
    private int LastPage => entries.Length == 0 ? 0 : (entries.Length - 1) / size;

    public void Update(AppSettings settings, DateTimeOffset now)
    {
        ApplySettings(settings, now);
        if (!enabled || now < deadline) return;

        if (page < LastPage) page++;
        else RestartCycle();
        Select();
        deadline = now.AddMinutes(minutes);
    }

    public bool TurnPage(AppSettings settings, int direction, DateTimeOffset now)
    {
        ApplySettings(settings, now);
        if (!enabled || direction == 0) return false;

        deadline = now.AddMinutes(minutes);
        int next = Math.Clamp(page + Math.Sign(direction), 0, LastPage);
        if (next == page) return false;
        page = next;
        Select();
        return true;
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
            Select();
        }
        if (changed || minutes != settings.VocabularyMinutes || enabled != settings.ShowVocabulary)
            deadline = now.AddMinutes(settings.VocabularyMinutes);
        minutes = settings.VocabularyMinutes;
        enabled = settings.ShowVocabulary;
    }

    private void RestartCycle()
    {
        page = 0;
        if (!random)
        {
            order = entries;
            return;
        }
        order = entries.ToArray();
        Random.Shared.Shuffle(order);
        int count = Math.Min(size, order.Length);
        if (order.Length > 1 && order.AsSpan(0, count).SequenceEqual(visible))
            (order[0], order[^1]) = (order[^1], order[0]);
    }

    private void Select()
    {
        int count = Math.Min(size, order.Length);
        // Align the last window to the last word so the panel keeps its height.
        int start = Math.Min(page * size, order.Length - count);
        visible = order.AsSpan(start, count).ToArray();
    }
}
