using System.IO.Compression;

namespace Mizu.Core.Services;

public static class EnglishLexicon
{
    private static readonly Lazy<WordTable> Words = new(Load);

    public static bool Contains(string word)
    {
        var text = word.AsSpan().Trim();
        if (text.Length == 0 || text.Length > 80) return false;
        if (text.Contains('’')) text = text.ToString().Replace('’', '\'');
        foreach (char c in text)
            if (!(char.IsAsciiLetter(c) || c is '\'' or '-')) return false;

        var table = Words.Value;
        int first = 0, last = table.Starts.Length - 2;
        while (first <= last)
        {
            int middle = first + (last - first) / 2;
            int start = table.Starts[middle];
            var entry = table.Text.AsSpan(start, table.Starts[middle + 1] - start - 1);
            int comparison = text.CompareTo(entry, StringComparison.OrdinalIgnoreCase);
            if (comparison == 0) return true;
            if (comparison < 0) last = middle - 1;
            else first = middle + 1;
        }
        return false;
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

    private sealed record WordTable(string Text, int[] Starts);
}
