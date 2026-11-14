using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mizu.Core.Services;

public sealed record PoetrySnapshot(string Content, string Author, string Origin, DateTimeOffset FetchedAt)
{
    public string Source { get; init; } = "今日诗词";
    public string Dynasty { get; init; } = "";
    public string DynastyMark => string.IsNullOrWhiteSpace(Dynasty) ? "—" : System.Globalization.StringInfo.GetNextTextElement(Dynasty.Trim());
    public string[] Lines(int count) => Regex.Matches(Content, @"[^，。！？；,!?;\r\n]+[，。！？；,!?;]?")
        .Select(m => m.Value.Trim()).Where(s => s.Length > 0).Take(Math.Clamp(count, 1, 2)).ToArray();
}

public sealed class PoetryService(HttpMessageHandler? handler = null, string? cachePath = null) : IDisposable
{
    private readonly HttpClient client = handler is null ? new() : new(handler);
    private readonly string file = cachePath ?? Path.Combine(SettingsStore.DataDirectory, "poetry-cache.json");
    private string? accessToken;
    private string TokenFile => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, "poetry-token.dat");
    public async Task<PoetrySnapshot> FetchAsync(CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(12));
        if (accessToken is null)
        {
            try { if (File.Exists(TokenFile)) accessToken = SettingsStore.Unprotect(File.ReadAllText(TokenFile)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("poetry.token.read", ex.GetType().Name); }
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "https://v2.jinrishici.com/token");
                tokenRequest.Headers.UserAgent.ParseAdd("Mizu/0.4.0");
                using var tokenResponse = await client.SendAsync(tokenRequest, deadline.Token); tokenResponse.EnsureSuccessStatusCode();
                using var tokenDoc = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync(deadline.Token), cancellationToken: deadline.Token);
                accessToken = tokenDoc.RootElement.GetProperty("data").GetString();
                if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 4096) throw new JsonException("Invalid poetry token");
                try { Directory.CreateDirectory(Path.GetDirectoryName(TokenFile)!); File.WriteAllText(TokenFile + ".tmp", SettingsStore.Protect(accessToken)); File.Move(TokenFile + ".tmp", TokenFile, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("poetry.token.save", ex.GetType().Name); }
            }
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://v2.jinrishici.com/sentence");
        request.Headers.UserAgent.ParseAdd("Mizu/0.4.0"); request.Headers.Add("X-User-Token", accessToken);
        request.Headers.CacheControl = new() { NoCache = true };
        using var response = await client.SendAsync(request, deadline.Token); response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(deadline.Token), cancellationToken: deadline.Token);
        return Parse(doc.RootElement, DateTimeOffset.UtcNow);
    }
    public static PoetrySnapshot Parse(JsonElement json, DateTimeOffset at)
    {
        bool rich = json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object;
        if (rich) json = data;
        var content = json.GetProperty("content").GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(content) || content.Length > 1000) throw new JsonException("Invalid poem");
        string Read(JsonElement node, string name) => node.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "")[..Math.Min(v.GetString()?.Length ?? 0, 160)] : "";
        if (json.TryGetProperty("origin", out var origin) && origin.ValueKind == JsonValueKind.Object) {
            var author = Read(origin, "author"); var title = Read(origin, "title"); var dynasty = Read(origin, "dynasty");
            if (string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(dynasty)) throw new JsonException("Incomplete poem provenance");
            return new(content, author, title, at) { Dynasty = dynasty };
        }
        if (rich) throw new JsonException("Missing poem provenance");
        return new(content, Read(json, "author"), Read(json, "origin"), at) { Dynasty = Read(json, "dynasty") };
    }
    private PoetrySnapshot? ReadCache(string path)
    {
        try {
            if (!File.Exists(path) || new FileInfo(path).Length > 16_000) return null;
            var poem = JsonSerializer.Deserialize<PoetrySnapshot>(File.ReadAllText(path));
            return poem is not null && !string.IsNullOrWhiteSpace(poem.Content) && poem.Content.Length <= 1000 && poem.FetchedAt <= DateTimeOffset.UtcNow ? poem : null;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { AppLog.Write("poetry.cache", ex.GetType().Name); return null; }
    }
    public PoetrySnapshot? Load() => new[] { ReadCache(file), ReadCache(file + ".local") }.Where(p => p is not null).OrderByDescending(p => p!.FetchedAt).FirstOrDefault();
    public void Save(PoetrySnapshot poem)
    {
        var path = poem.Source == "内置公版诗库" ? file + ".local" : file;
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(poem)); File.Move(path + ".tmp", path, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Write("poetry.cache", ex.GetType().Name); }
    }
    public void Dispose() => client.Dispose();
}
