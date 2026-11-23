using System.Text.RegularExpressions;

namespace Mizu.Core.Services;

public sealed record FirebaseConfig(string ProjectId, string ApiKey)
{
    public const int MaximumConfigLength = 64 * 1024;
    private static readonly Regex ProjectPattern = new(@"\A[a-z][a-z0-9-]{4,28}[a-z0-9]\z", RegexOptions.CultureInvariant);
    private static readonly Regex KeyPattern = new(@"\AAIza[A-Za-z0-9_-]{35}\z", RegexOptions.CultureInvariant);

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => ProjectId is not null && ApiKey is not null && ProjectPattern.IsMatch(ProjectId) && KeyPattern.IsMatch(ApiKey);

    public static FirebaseConfig Parse(string text)
    {
        if (text.Length > MaximumConfigLength) throw new FormatException("Firebase 配置最多 64 KB。");
        if (Regex.IsMatch(text, @"private_key|BEGIN [A-Z ]*PRIVATE KEY|service_account", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new FormatException("请选择 Firebase 网页客户端配置。");

        // Match only quoted values with the two expected field names. The rest
        // of a Firebase SDK snippet is inert text.
        static string? Field(string block, string name)
        {
            var pattern = $$"""(?:^|[\s{,])(?:"{{name}}"|'{{name}}'|{{name}})\s*:\s*(?:"([^"]*)"|'([^']*)')""";
            var matches = Regex.Matches(block, pattern, RegexOptions.CultureInvariant);
            if (matches.Count != 1) return null;
            var match = matches[0];
            return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        }
        var candidates = new HashSet<FirebaseConfig>();
        foreach (Match block in Regex.Matches(text, @"\{[^{}]*\}"))
        {
            var projectId = Field(block.Value, "projectId");
            var apiKey = Field(block.Value, "apiKey");
            if (projectId is null || apiKey is null) continue;
            var config = new FirebaseConfig(projectId, apiKey);
            if (config.IsValid) candidates.Add(config);
        }
        return candidates.Count switch
        {
            1 => candidates.Single(),
            0 => throw new FormatException("找不到带有效 projectId 和 apiKey 的 Firebase 网页配置。"),
            _ => throw new FormatException("文件包含多套有效配置，请只导入要连接的那一套。")
        };
    }

    public override string ToString() => nameof(FirebaseConfig);
}
