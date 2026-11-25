using System.IO.Compression;

namespace Mizu.Core.Services;

public static class EnglishLexicon
{
    private static readonly Lazy<WordTable> Words = new(Load);

    public static bool Contains(string word)
    {
        var text = Normalize(word);
        if (text.IsEmpty) return false;

        var table = Words.Value;
        int index = table.LowerBound(text);
        return index < table.Count && text.Equals(table.At(index), StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> FindByPrefix(string prefix, int limit)
    {
        var text = Normalize(prefix);
        if (text.IsEmpty || limit <= 0) return [];

        var table = Words.Value;
        int first = table.LowerBound(text);
        if (text.Length == 1)
            return first < table.Count && text.Equals(table.At(first), StringComparison.OrdinalIgnoreCase)
                ? [table.At(first).ToString()] : [];

        var matches = new List<string>();
        for (int index = first; index < table.Count && matches.Count < limit; index++)
        {
            var word = table.At(index);
            if (!word.StartsWith(text, StringComparison.OrdinalIgnoreCase)) break;
            matches.Add(word.ToString());
        }
        return matches;
    }

    private static ReadOnlySpan<char> Normalize(string word)
    {
        var text = word.AsSpan().Trim();
        if (text.Length > 80) return [];
        foreach (char c in text)
            if (!(char.IsAsciiLetter(c) || c is '\'' or '’' or '-')) return [];
        return text.Contains('’') ? text.ToString().Replace('’', '\'') : text;
    }

    private static WordTable Load()
    {
        using var resource = typeof(EnglishLexicon).Assembly.GetManifestResourceStream("Mizu.EnglishWords")
            ?? throw new InvalidOperationException("English word list is missing");
        using var compressed = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new StreamReader(compressed, System.Text.Encoding.ASCII);
        string text = reader.ReadToEnd();
        int lines = 0;
        foreach (char c in text) if (c == '\n') lines++;
        var starts = new int[lines + 1];
        int row = 1;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') starts[row++] = i + 1;
        return new(text, starts);
    }

    private sealed record WordTable(string Text, int[] Starts)
    {
        public int Count => Starts.Length - 1;
        public ReadOnlySpan<char> At(int index) => Text.AsSpan(Starts[index], Starts[index + 1] - Starts[index] - 1);

        public int LowerBound(ReadOnlySpan<char> text)
        {
            int first = 0, end = Count;
            while (first < end)
            {
                int middle = first + (end - first) / 2;
                if (At(middle).CompareTo(text, StringComparison.OrdinalIgnoreCase) < 0) first = middle + 1;
                else end = middle;
            }
            return first;
        }
    }
}
