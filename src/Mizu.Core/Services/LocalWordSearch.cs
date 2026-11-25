namespace Mizu.Core.Services;

public readonly record struct LocalWordMatch(string Word, string? Meaning, bool Saved);

public static class LocalWordSearch
{
    private const int ResultLimit = 8;
    private const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;

    public static IReadOnlyList<LocalWordMatch> Find(IReadOnlyList<VocabularyEntry> entries, string query)
    {
        query = query.Trim().Replace('’', '\'');
        if (query.Length == 0) return [];

        var matches = entries.Select(entry => (Entry: entry, Rank: Rank(entry, query)))
            .Where(match => match.Rank >= 0)
            .OrderBy(match => match.Rank)
            .Take(ResultLimit)
            .Select(match => new LocalWordMatch(match.Entry.Word, match.Entry.Meaning, true))
            .ToList();
        int available = ResultLimit - matches.Count;
        if (available == 0) return matches;

        var saved = matches.Select(match => match.Word.Replace('’', '\'')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string word in EnglishLexicon.FindByPrefix(query, available + saved.Count))
        {
            if (!saved.Contains(word)) matches.Add(new(word, null, false));
            if (matches.Count == ResultLimit) break;
        }
        return matches;
    }

    private static int Rank(VocabularyEntry entry, string query)
    {
        string word = entry.Word.Replace('’', '\'');
        if (word.Equals(query, IgnoreCase)) return 0;
        if (word.StartsWith(query, IgnoreCase)) return 1;
        if (word.Contains(query, IgnoreCase) || entry.Meaning?.Contains(query, IgnoreCase) == true) return 2;
        return -1;
    }
}
