using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mizu.Core.Services;

public static class VocabularyExport
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(IReadOnlyList<VocabularyEntry> entries, string extension)
    {
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(entries, JsonOptions);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("文件扩展名应为 .csv 或 .json");

        var csv = new StringBuilder("word,meaning\r\n");
        foreach (var entry in entries)
            csv.Append(Quote(entry.Word)).Append(',').Append(Quote(entry.Meaning)).Append("\r\n");
        return csv.ToString();
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
