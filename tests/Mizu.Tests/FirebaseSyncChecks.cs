using System.Net;
using System.Text;
using System.Text.Json;
using Mizu.Core.Services;

internal static class FirebaseSyncChecks
{
    public static async Task<int> RunAsync()
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            count++;
        }
        void Reject<T>(Action operation, string message) where T : Exception
        {
            try { operation(); } catch (T) { count++; return; }
            throw new InvalidOperationException(message);
        }
        const string project = "demo-mizu";
        const string uid = "test-user";
        var key = "AIza" + new string('x', 35);
        var config = FirebaseConfig.Parse($"const firebaseConfig = {{apiKey: '{key}', projectId: \"{project}\"}}; initializeApp(firebaseConfig);");
        Check(config.ApiKey == key && config.ProjectId == project, "Firebase Web SDK snippets are parsed as data");
        Check(FirebaseConfig.Parse(JsonSerializer.Serialize(new { apiKey = key, projectId = project })) == config, "JSON config works");
        Check(FirebaseConfig.Parse($"{{apiKey: '{key}', projectId: '{project}'}}\n{{apiKey: 'EXAMPLE', projectId: '{project}'}}") == config,
            "A placeholder snippet does not invalidate a real configuration in the same file");
        Reject<FormatException>(() => FirebaseConfig.Parse($"{{apiKey: '{key}', projectId: 'abc.example.com'}}"), "Malformed project ids must be rejected");
        Reject<FormatException>(() => FirebaseConfig.Parse($"{{apiKey: '{key}', projectId: '{project}', private_key: 'test'}}"), "Service-account material must be rejected");
        Reject<FormatException>(() => FirebaseConfig.Parse($"{{apiKey: '{key}', projectId: '{project}', projectId: 'alternate'}}"), "Ambiguous project ids must be rejected");
        Reject<FormatException>(() => FirebaseConfig.Parse($"{{apiKey: '{key}', projectId: '{project}'}} {{apiKey: '{key}', projectId: 'other-project'}}"),
            "Separate plausible configurations must not be combined");

        var original = new AppSettings
        {
            ProtectedPushToken = "local-push-token", ProtectedFirebaseSync = "local-sync-token",
            Left = 123, Theme = "navy", ShowVocabulary = true, VocabularyColumns = 4,
            Vocabulary = [new() { Word = "serene", Meaning = "宁静" }],
            Todos = [new() { Id = "todo-1", Text = "完成", Completed = true }]
        };
        var snapshot = SyncSnapshot.Capture(original);
        var data = snapshot.Encode();
        var serialized = Encoding.UTF8.GetString(data);
        Check(!serialized.Contains("local-push-token") && !serialized.Contains("local-sync-token") &&
            !serialized.Contains("\"Left\""), "Sensitive settings and coordinates are excluded");
        var decoded = SyncSnapshot.Decode(data);
        var destination = new AppSettings { Left = 45, ProtectedPushToken = "destination", ShowCpu = false, NetworkProbeTarget = "8.8.8.8" };
        decoded.ApplyTo(destination);
        destination.Validate();
        Check(destination.Left == 45 && !destination.ShowCpu && destination.ProtectedPushToken == "destination" &&
            destination.NetworkProbeTarget == "8.8.8.8", "Applying a synced snapshot keeps device-local settings");
        Check(destination.Theme == "navy" && destination.Todos[0].Id == "todo-1" && destination.Todos[0].Completed &&
            destination.VocabularyColumns == 4 && destination.Vocabulary[0].Meaning == "宁静", "Lists and display preferences transfer");
        Check(SyncSnapshot.Capture(destination).Encode().SequenceEqual(data), "A valid snapshot has a stable hash after application");
        Reject<JsonException>(() => SyncSnapshot.Decode("{}"u8), "Incomplete snapshots must be rejected");
        Reject<InvalidDataException>(() => (snapshot with { Display = snapshot.Display with { WordColumns = 99 } }).Encode(), "Invalid layout values must be rejected");
        Check(SyncSnapshot.DecideUpload("a", null, null) == CloudUploadDecision.Upload &&
            SyncSnapshot.DecideUpload("a", "a", null) == CloudUploadDecision.AlreadyCurrent, "First backup creates or recognizes the cloud document");
        Check(SyncSnapshot.DecideUpload("b", "a", "a") == CloudUploadDecision.Upload &&
            SyncSnapshot.DecideUpload("a", "b", "a") == CloudUploadDecision.CloudChanged, "Only the already recognized cloud version can be replaced automatically");
        Check(SyncSnapshot.DecideUpload("b", "c", "a") == CloudUploadDecision.CloudChanged &&
            SyncSnapshot.DecideUpload("b", "c", null) == CloudUploadDecision.CloudChanged, "An unknown cloud backup is protected");
        var wordsOnly = snapshot.ClearLocalData(words: true, todos: false, display: false);
        Check(wordsOnly.Words.Length == 0 && wordsOnly.Todos.SequenceEqual(snapshot.Todos) &&
            wordsOnly.Appearance == snapshot.Appearance, "Cleaning vocabulary leaves todos and preferences alone");
        var restOnly = snapshot.ClearLocalData(words: false, todos: true, display: true);
        Check(restOnly.Words.SequenceEqual(snapshot.Words) && restOnly.Todos.Length == 0 &&
            restOnly.Appearance.Theme == "light", "Selected display preferences reset without cleaning the word book");
        var oldSettings = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Check(!oldSettings.AutoCloudBackup && oldSettings.CloudBackupIntervalMinutes == 60, "Existing users start with automatic backup disabled");
        oldSettings.CloudBackupIntervalMinutes = 9999;
        oldSettings.Validate();
        Check(oldSettings.CloudBackupIntervalMinutes == 720, "Automatic backup interval is bounded");

        string Token(string audience) => "header." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
        {
            aud = audience, iss = "https://securetoken.google.com/" + audience, sub = uid
        })).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".signature";

        string jwt = Token(project);
        const string revision = "2026-10-01T12:13:14.123456Z";
        string document = JsonSerializer.Serialize(new
        {
            updateTime = revision,
            fields = new { schemaVersion = new { integerValue = "1" }, payload = new { bytesValue = Convert.ToBase64String(data) } }
        });
        using var http = new HttpClient(new StubHandler(async (request, token) =>
        {
            var uri = request.RequestUri!;
            Check(uri.Scheme == "https", "Firebase is contacted via HTTPS");
            if (uri.Host == "identitytoolkit.googleapis.com")
            {
                Check(request.Method == HttpMethod.Post && uri.AbsolutePath.EndsWith("accounts:signInWithPassword"), "Sign-in uses the official Auth route");
                return Response(HttpStatusCode.OK, JsonSerializer.Serialize(new { localId = uid, refreshToken = "test-refresh", idToken = jwt }));
            }
            if (uri.Host == "securetoken.googleapis.com")
            {
                Check(request.Content is not null && (await request.Content.ReadAsStringAsync(token)).Contains("grant_type=refresh_token"), "Refresh uses the saved refresh token");
                return Response(HttpStatusCode.OK, JsonSerializer.Serialize(new { user_id = uid, refresh_token = "renewed-token", id_token = jwt }));
            }
            Check(uri.Host == "firestore.googleapis.com" && request.Headers.Authorization?.Parameter == jwt,
                "Firestore requests use the Auth ID token");
            if (request.Method == HttpMethod.Get)
            {
                Check(uri.AbsolutePath.EndsWith($"/documents/mizuSync/{uid}"), "Reads use the authorized user path");
                return Response(HttpStatusCode.OK, document);
            }
            using var commit = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var write = commit.RootElement.GetProperty("writes").EnumerateArray().Single();
            Check(uri.AbsolutePath.EndsWith("/documents:commit") &&
                write.GetProperty("update").GetProperty("name").GetString()!.EndsWith($"/documents/mizuSync/{uid}"),
                "Commits only update the authorized user path");
            var guard = write.GetProperty("currentDocument");
            if (guard.TryGetProperty("exists", out var exists) && !exists.GetBoolean()) return Response(HttpStatusCode.OK, document);
            Check(guard.GetProperty("updateTime").GetString() == revision, "Updates retain the exact conditional timestamp");
            return Response(HttpStatusCode.PreconditionFailed, """{"error":{"status":"FAILED_PRECONDITION","message":"untrusted response"}}""");
        }));
        var client = new FirebaseSyncClient(http);
        var session = await client.SignInAsync(config, "user@example.test", "example-password", default);
        var protectedSession = session.Protect();
        Check(protectedSession is not null && !protectedSession.Contains("test-refresh") &&
            FirebaseConnection.Restore(protectedSession) == session, "Session restores through Windows DPAPI");
        var access = await client.RefreshAsync(session, default);
        Check(access.Connection.RefreshToken == "renewed-token", "Refreshed credentials remain usable");
        var cloud = await client.DownloadAsync(access, default);
        Check(cloud?.Revision == revision && cloud.Snapshot.Encode().SequenceEqual(data), "Cloud document preserves synced content");
        string folder = Path.Combine(Path.GetTempPath(), "Mizu-backup-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new SettingsStore(Path.Combine(folder, "settings.json"));
            var setup = new AppSettings { AutoCloudBackup = true, Left = 45, ProtectedPushToken = "local-only" };
            decoded.ApplyTo(setup);
            setup.Vocabulary.Add(new() { Word = "only-local" });
            var cleanupBackup = Path.Combine(folder, "mizu-cleanup-backup.json");
            var localData = SyncSnapshot.Capture(setup);
            var reset = localData.ClearLocalData(words: true, todos: true, display: true);
            store.SaveSyncState(setup, session, reset, cleanupBackup, pauseAutomaticBackup: true);
            var reloaded = store.Load();
            Check(!reloaded.AutoCloudBackup && reloaded.Vocabulary.Count == 0 && reloaded.Todos.Count == 0 &&
                reloaded.Theme == "light" && FirebaseConnection.Restore(reloaded.ProtectedFirebaseSync) == session,
                "Cleanup persists the selected changes, retains login and pauses automatic upload");
            Check(SyncSnapshot.Decode(File.ReadAllBytes(cleanupBackup)).Encode().SequenceEqual(localData.Encode()),
                "Cleanup preserves the data present before clearing");
            store.SaveSyncState(reloaded, session, cloud!.Snapshot);
            reloaded = store.Load();
            Check(reloaded.Theme == "navy" && reloaded.Vocabulary.Single().Meaning == "宁静" &&
                reloaded.Todos.Single().Completed && reloaded.Left == 45 && reloaded.ProtectedPushToken == "local-only",
                "The downloaded cloud backup restores words, todo completion and display without changing local-only settings");
            Check(SyncSnapshot.Decode(File.ReadAllBytes(cleanupBackup)).Words.Any(word => word.Word == "only-local") &&
                SyncSnapshot.Decode(File.ReadAllBytes(Path.Combine(folder, "mizu-sync-backup.json"))).Words.Length == 0,
                "Cloud restore keeps a separate undo copy without replacing the pre-cleanup copy");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
        await client.UploadAsync(access, data, null, default);
        bool stopped = false;
        try { await client.UploadAsync(access, data, revision, default); }
        catch (FirebaseRequestException ex) { stopped = !ex.Message.Contains("untrusted response"); }
        Check(stopped, "Concurrent-write refusal is surfaced without the raw server error");
        jwt = Token("other-project");
        bool mismatch = false;
        try { await client.SignInAsync(config, "user@example.test", "example-password", default); }
        catch (InvalidDataException) { mismatch = true; }
        Check(mismatch, "Auth credentials for another project cannot be used with this configuration");
        return count;
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
