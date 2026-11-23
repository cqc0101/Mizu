using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mizu.Core.Services;

public sealed record FirebaseConnection(FirebaseConfig Config, string Email, string UserId, string RefreshToken,
    string? LastSyncHash = null, DateTimeOffset? LastSyncAt = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Config is { IsValid: true } && !string.IsNullOrWhiteSpace(Email) && ValidUserId(UserId) &&
        !string.IsNullOrWhiteSpace(RefreshToken) && RefreshToken.Length <= 8192;

    internal static bool ValidUserId(string? id) =>
        id is not null && Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{1,128}\z", RegexOptions.CultureInvariant) &&
        !(id.StartsWith("__", StringComparison.Ordinal) && id.EndsWith("__", StringComparison.Ordinal));

    public static FirebaseConnection? Restore(string? protectedConnection)
    {
        try
        {
            var json = SettingsStore.Unprotect(protectedConnection);
            var connection = json is null ? null : JsonSerializer.Deserialize<FirebaseConnection>(json);
            return connection is { IsValid: true } ? connection : null;
        }
        catch (JsonException) { return null; }
    }

    public string? Protect() => SettingsStore.Protect(JsonSerializer.Serialize(this));
    public override string ToString() => nameof(FirebaseConnection);
}

public sealed record FirebaseAccess(FirebaseConnection Connection, string IdToken)
{
    public override string ToString() => nameof(FirebaseAccess);
}
public sealed record FirestoreSnapshot(SyncSnapshot Snapshot, string Revision);
public sealed class FirebaseRequestException(string message) : IOException(message);

public sealed class FirebaseSyncClient
{
    private const string FirestoreRoot = "https://firestore.googleapis.com/v1/";
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(25)
    };
    private readonly HttpClient http;

    public FirebaseSyncClient(HttpClient? http = null) => this.http = http ?? SharedClient;

    public async Task<FirebaseConnection> SignInAsync(FirebaseConfig config, string email, string password, CancellationToken token)
    {
        if (!config.IsValid || string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            throw new FormatException("请填写项目配置、Firebase Auth 邮箱和密码。");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" + config.ApiKey)
        {
            Content = JsonContent.Create(new { email = email.Trim(), password, returnSecureToken = true })
        };
        using var response = await SendAsync(request, token).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw AuthError(doc.RootElement);
        var result = doc.RootElement;
        var connection = new FirebaseConnection(config, email.Trim(), String(result, "localId"), String(result, "refreshToken"));
        ValidateIdentity(connection, String(result, "idToken"));
        return connection;
    }

    public async Task<FirebaseAccess> RefreshAsync(FirebaseConnection connection, CancellationToken token)
    {
        if (!connection.IsValid) throw new FormatException("请重新登录 Firebase。");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "https://securetoken.googleapis.com/v1/token?key=" + connection.Config.ApiKey)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = connection.RefreshToken
            })
        };
        using var response = await SendAsync(request, token).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw AuthError(doc.RootElement);
        var result = doc.RootElement;
        string idToken = String(result, "id_token");
        var renewed = connection with { RefreshToken = String(result, "refresh_token") };
        if (String(result, "user_id") != connection.UserId) throw new InvalidDataException("Firebase 登录返回了不同的账号。");
        ValidateIdentity(renewed, idToken);
        return new(renewed, idToken);
    }

    public async Task<FirestoreSnapshot?> DownloadAsync(FirebaseAccess access, CancellationToken token)
    {
        string document = DatabasePath(access.Connection) + "/documents/mizuSync/" + access.Connection.UserId;
        using var request = AuthorizedRequest(HttpMethod.Get, document, access.IdToken);
        using var response = await SendAsync(request, token).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound &&
            String(Field(doc.RootElement, "error"), "message").StartsWith("Document ", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!response.IsSuccessStatusCode) throw FirestoreError(response.StatusCode, doc.RootElement);
        var fields = Field(doc.RootElement, "fields");
        if (String(Field(fields, "schemaVersion"), "integerValue") != SyncSnapshot.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidDataException("云端同步格式不兼容，请检查 Mizu 版本。");
        string revision = String(doc.RootElement, "updateTime");
        if (!DateTimeOffset.TryParse(revision, System.Globalization.CultureInfo.InvariantCulture, out _))
            throw new InvalidDataException("云端版本信息无效。");
        var data = Convert.FromBase64String(String(Field(fields, "payload"), "bytesValue"));
        return new(SyncSnapshot.Decode(data), revision);
    }

    public async Task UploadAsync(FirebaseAccess access, byte[] data, string? expectedRevision, CancellationToken token)
    {
        if (data.Length > SyncSnapshot.MaximumBytes) throw new InvalidDataException("同步数据超过容量限制。");
        string database = DatabasePath(access.Connection);
        using var request = AuthorizedRequest(HttpMethod.Post, database + "/documents:commit", access.IdToken);
        object precondition = expectedRevision is null ? new { exists = false } : new { updateTime = expectedRevision };
        request.Content = JsonContent.Create(new
        {
            writes = new[]
            {
                new
                {
                    currentDocument = precondition,
                    update = new
                    {
                        name = database + "/documents/mizuSync/" + access.Connection.UserId,
                        fields = new
                        {
                            schemaVersion = new { integerValue = SyncSnapshot.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                            payload = new { bytesValue = Convert.ToBase64String(data) }
                        }
                    }
                }
            }
        });
        using var response = await SendAsync(request, token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode) return;
        using var doc = await ReadJsonAsync(response, token).ConfigureAwait(false);
        throw FirestoreError(response.StatusCode, doc.RootElement, writing: true);
    }

    private static string DatabasePath(FirebaseConnection connection)
    {
        if (!connection.IsValid) throw new FormatException("请重新登录 Firebase。");
        return $"projects/{connection.Config.ProjectId}/databases/(default)";
    }

    private static HttpRequestMessage AuthorizedRequest(HttpMethod method, string path, string idToken)
    {
        var request = new HttpRequestMessage(method, FirestoreRoot + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
        return request;
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
        http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new InvalidDataException("Firebase 响应超过容量限制。");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        int count;
        while ((count = await stream.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > MaximumResponseBytes) throw new InvalidDataException("Firebase 响应超过容量限制。");
            buffer.Write(chunk, 0, count);
        }
        return JsonDocument.Parse(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
    }

    private static void ValidateIdentity(FirebaseConnection connection, string idToken)
    {
        var parts = idToken.Split('.');
        if (!connection.IsValid || parts.Length != 3 || idToken.Length > 16_384)
            throw new InvalidDataException("Firebase 登录响应无效。");
        string payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        using var jwt = JsonDocument.Parse(Convert.FromBase64String(payload));
        if (String(jwt.RootElement, "aud") != connection.Config.ProjectId ||
            String(jwt.RootElement, "iss") != "https://securetoken.google.com/" + connection.Config.ProjectId ||
            String(jwt.RootElement, "sub") != connection.UserId)
            throw new InvalidDataException("Firebase 项目 ID 与登录账号所属项目不匹配。");
    }

    private static JsonElement Field(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) ? value : default;
    private static string String(JsonElement node, string name) =>
        Field(node, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";

    private static FirebaseRequestException AuthError(JsonElement json) => new(String(Field(json, "error"), "message").Split(' ', 2)[0] switch
    {
        "EMAIL_NOT_FOUND" or "INVALID_PASSWORD" or "INVALID_LOGIN_CREDENTIALS" or "INVALID_EMAIL" => "请核对 Firebase Auth 的邮箱和密码。",
        "OPERATION_NOT_ALLOWED" or "PASSWORD_LOGIN_DISABLED" => "请在 Firebase Authentication 中启用电子邮件／密码登录。",
        "API_KEY_INVALID" or "API_KEY_SERVICE_BLOCKED" or "PROJECT_NUMBER_MISMATCH" => "请检查 Firebase 网页 API Key、项目及 API 限制。",
        "INVALID_REFRESH_TOKEN" or "TOKEN_EXPIRED" or "USER_DISABLED" or "USER_NOT_FOUND" => "Firebase 登录已失效或账号不可用，请退出后重新登录。",
        "TOO_MANY_ATTEMPTS_TRY_LATER" => "Firebase 暂时限制登录请求，请稍后重试。",
        _ => "Firebase 登录失败，请检查 Auth 配置与账号。"
    });

    private static FirebaseRequestException FirestoreError(HttpStatusCode status, JsonElement json, bool writing = false)
    {
        string code = String(Field(json, "error"), "status");
        if (writing && (status is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed ||
            code is "FAILED_PRECONDITION" or "ALREADY_EXISTS"))
            return new("云端版本或写入条件已变化，请重新同步。");
        if (status == HttpStatusCode.Forbidden)
            return new("Firestore 拒绝访问，请检查该项目的安全规则。");
        if (status == HttpStatusCode.NotFound)
            return new("找不到默认 Firestore 数据库，请在 Firebase 控制台创建。");
        if (status == HttpStatusCode.Unauthorized) return new("Firestore 登录已失效，请退出后重新登录。");
        return new("Firestore 请求失败，请稍后重试。");
    }
}
