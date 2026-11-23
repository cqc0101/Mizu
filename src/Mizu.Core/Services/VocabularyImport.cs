using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace Mizu.Core.Services;

public static class VocabularyImport
{
    public static IReadOnlyList<VocabularyEntry> Parse(string text, string extension)
    {
        if (text.Length > 4 * 1024 * 1024) throw new FormatException("文件最多 4 MB");
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            var items = JsonSerializer.Deserialize<List<VocabularyEntry>>(text.TrimStart('\uFEFF'), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new FormatException("JSON 必须是词条数组");
            if (items.Any(w => w is null || string.IsNullOrWhiteSpace(w.Word))) throw new FormatException("词条必须包含 Word，Meaning 为可选释义");
            return items;
        }
        string delimiter = extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) ? "," : "\t";
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        using var parser = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        parser.SetDelimiters(delimiter);
        var result = new List<VocabularyEntry>();
        bool firstRecord = true;
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? [];
            if (fields.Length == 0 || string.IsNullOrWhiteSpace(fields[0])) continue;
            if (fields.Length > 2) throw new FormatException("每行应包含单词和释义两列；CSV 中包含逗号的释义请用双引号包围");
            bool isHeader = firstRecord && fields[0].Equals("word", StringComparison.OrdinalIgnoreCase) &&
                fields.Length == 2 && fields[1].Equals("meaning", StringComparison.OrdinalIgnoreCase);
            firstRecord = false;
            if (!isHeader) result.Add(new() { Word = fields[0], Meaning = fields.Length == 2 ? fields[1] : "" });
        }
        return result;
    }
    public static bool TryAdd(AppSettings settings, string word, string meaning, out string message)
    {
        word = word.Trim(); meaning = meaning.Trim();
        if (word.Length == 0) { message = "请先输入单词"; return false; }
        if (word.Length > 80 || meaning.Length > 200) { message = "单词最多 80 字符，释义最多 200 字符"; return false; }
        if (settings.Vocabulary.Count >= 500) { message = "最多保存 500 个单词"; return false; }
        if (settings.Vocabulary.Any(w => w.Word.Equals(word, StringComparison.OrdinalIgnoreCase))) { message = "单词已存在"; return false; }
        settings.Vocabulary.Add(new() { Word = word, Meaning = meaning }); message = "单词已保存"; return true;
    }
}
