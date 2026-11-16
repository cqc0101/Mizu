using Mizu.Core.Services;

namespace Mizu.Core;

public sealed class VocabularyRotation
{
    private VocabularyEntry[] entries = [];
    private VocabularyEntry[] visible = [];
    private int start, size, seconds;
    private bool random, enabled;
    private DateTimeOffset deadline;
    public IReadOnlyList<VocabularyEntry> Visible => visible;

    public void Update(AppSettings settings, DateTimeOffset now)
    {
        bool changed = !entries.SequenceEqual(settings.Vocabulary) || size != settings.VocabularyVisibleRows || random != settings.VocabularyRandom;
        entries = settings.Vocabulary.ToArray(); size = settings.VocabularyVisibleRows; random = settings.VocabularyRandom;
        if (changed) { start = 0; Select(); }
        if (changed || seconds != settings.VocabularyIntervalSeconds || enabled != settings.ShowVocabulary)
            deadline = now.AddSeconds(settings.VocabularyIntervalSeconds);
        seconds = settings.VocabularyIntervalSeconds; enabled = settings.ShowVocabulary;
        if (enabled && now >= deadline) Advance(1, now);
    }
    public void Advance(int direction, DateTimeOffset now)
    {
        if (entries.Length > 0) start = (start + direction * Math.Min(size, entries.Length) % entries.Length + entries.Length) % entries.Length;
        Select(); deadline = now.AddSeconds(Math.Max(5, seconds));
    }
    private void Select()
    {
        int count = Math.Min(size, entries.Length);
        if (!random) { visible = Enumerable.Range(0, count).Select(i => entries[(start + i) % entries.Length]).ToArray(); return; }
        var indices = Enumerable.Range(0, entries.Length).ToArray(); Random.Shared.Shuffle(indices);
        // Avoid showing the exact same ordered group when alternatives exist.
        if (entries.Length > 1 && count > 0 && visible.Length == count && indices.Take(count).Select(i => entries[i]).SequenceEqual(visible))
            (indices[0], indices[^1]) = (indices[^1], indices[0]);
        visible = indices.Take(count).Select(i => entries[i]).ToArray();
    }
}
