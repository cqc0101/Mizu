using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mizu.Core.Services;

public enum CloudUploadDecision { AlreadyCurrent, Upload, CloudChanged }

public sealed record SyncTodo(string Id, string Text, bool Completed);
public sealed record SyncWord(string Word, string Meaning);
public sealed record SyncAppearance(string Theme, double Scale, double GlassOpacity, double CornerRadius,
    double TextScale, int TextWeight, bool CompactMode, bool SmoothResize);
public sealed record SyncDisplay(WidgetSection[] Order, WidgetSection[] Visible, int TodoRows, bool HideCompletedTodos,
    int WordRows, int WordColumns, int WordSpacing, int MeaningFontSize, bool HideMeaning, bool RandomWords, int WordMinutes);

public sealed record SyncSnapshot(SyncAppearance Appearance, SyncDisplay Display, SyncTodo[] Todos, SyncWord[] Words)
{
    public const int Version = 1;
    public const int MaximumBytes = 1_000_000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, RespectRequiredConstructorParameters = true
    };

    public static SyncSnapshot Capture(AppSettings settings)
    {
        var visible = new List<WidgetSection>();
        if (settings.ShowClock) visible.Add(WidgetSection.Clock);
        if (settings.ShowWeather) visible.Add(WidgetSection.Weather);
        if (settings.ShowDevices) visible.Add(WidgetSection.Devices);
        if (settings.ShowGpuMemory) visible.Add(WidgetSection.GpuMemory);
        if (settings.ShowNetwork) visible.Add(WidgetSection.Network);
        if (settings.ShowMedia) visible.Add(WidgetSection.Media);
        if (settings.ShowPoetry) visible.Add(WidgetSection.Poetry);
        if (settings.ShowTodo) visible.Add(WidgetSection.Todo);
        if (settings.ShowVocabulary) visible.Add(WidgetSection.Vocabulary);
        // Hardware sampling switches depend on the current machine.
        return new(
            new(settings.Theme, settings.Scale, settings.GlassOpacity, settings.CornerRadius,
                settings.TextScale, settings.TextWeight, settings.CompactMode, settings.SmoothResize),
            new(settings.SectionOrder.ToArray(), visible.ToArray(), settings.TodoVisibleRows, settings.HideCompletedTodos,
                settings.VocabularyVisibleRows, settings.VocabularyColumns, settings.VocabularySpacing, settings.VocabularyMeaningFontSize,
                settings.VocabularyHideMeaning, settings.VocabularyRandom, settings.VocabularyMinutes),
            settings.Todos.Select(t => new SyncTodo(t.Id, t.Text, t.Completed)).ToArray(),
            settings.Vocabulary.Select(w => new SyncWord(w.Word, w.Meaning)).ToArray());
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Theme = Appearance.Theme;
        settings.Scale = Appearance.Scale;
        settings.GlassOpacity = Appearance.GlassOpacity;
        settings.CornerRadius = Appearance.CornerRadius;
        settings.TextScale = Appearance.TextScale;
        settings.TextWeight = Appearance.TextWeight;
        settings.CompactMode = Appearance.CompactMode;
        settings.SmoothResize = Appearance.SmoothResize;
        settings.SectionOrder = Display.Order.ToList();
        var visible = Display.Visible.ToHashSet();
        settings.ShowClock = visible.Contains(WidgetSection.Clock);
        settings.ShowWeather = visible.Contains(WidgetSection.Weather);
        settings.ShowDevices = visible.Contains(WidgetSection.Devices);
        settings.ShowGpuMemory = visible.Contains(WidgetSection.GpuMemory);
        settings.ShowNetwork = visible.Contains(WidgetSection.Network);
        settings.ShowMedia = visible.Contains(WidgetSection.Media);
        settings.ShowPoetry = visible.Contains(WidgetSection.Poetry);
        settings.ShowTodo = visible.Contains(WidgetSection.Todo);
        settings.ShowVocabulary = visible.Contains(WidgetSection.Vocabulary);
        settings.TodoVisibleRows = Display.TodoRows;
        settings.HideCompletedTodos = Display.HideCompletedTodos;
        settings.VocabularyVisibleRows = Display.WordRows;
        settings.VocabularyColumns = Display.WordColumns;
        settings.VocabularySpacing = Display.WordSpacing;
        settings.VocabularyMeaningFontSize = Display.MeaningFontSize;
        settings.VocabularyHideMeaning = Display.HideMeaning;
        settings.VocabularyRandom = Display.RandomWords;
        settings.VocabularyIntervalMinutes = Display.WordMinutes;
        settings.Todos = Todos.Select(t => new TodoItem { Id = t.Id, Text = t.Text, Completed = t.Completed }).ToList();
        settings.Vocabulary = Words.Select(w => new VocabularyEntry { Word = w.Word, Meaning = w.Meaning }).ToList();
    }

    public byte[] Encode()
    {
        Validate();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("词条与待办超过云端同步容量。");
        return bytes;
    }

    public static SyncSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("同步数据超过容量限制。");
        var snapshot = JsonSerializer.Deserialize<SyncSnapshot>(bytes, JsonOptions)
            ?? throw new InvalidDataException("同步数据为空。");
        snapshot.Validate();
        return snapshot;
    }

    private void Validate()
    {
        if (Appearance is null || Display is null || Todos is null || Words is null ||
            Appearance.Theme is not ("light" or "dark" or "navy" or "pink" or "lavender") ||
            Appearance.Scale is < .4 or > 1.2 || Appearance.GlassOpacity is < .15 or > .95 ||
            Appearance.CornerRadius is < 0 or > 64 || Appearance.TextScale is < .85 or > 1.25 ||
            Appearance.TextWeight is < 300 or > 700 || Display.Order is null || Display.Visible is null ||
            Display.Order.Length != Enum.GetValues<WidgetSection>().Length || Display.Order.Distinct().Count() != Display.Order.Length ||
            Display.Order.Any(kind => !Enum.IsDefined(kind)) || Display.Visible.Length > Display.Order.Length ||
            Display.Visible.Any(kind => !Enum.IsDefined(kind) || kind == WidgetSection.Metrics) ||
            Display.TodoRows is < 1 or > 8 || Display.WordRows is < 1 or > WidgetLayout.MaximumVocabularyRows ||
            Display.WordColumns is < 1 or > WidgetLayout.MaximumVocabularyColumns || Display.WordSpacing is < 0 or > 40 ||
            Display.MeaningFontSize is < AppSettings.MinimumVocabularyMeaningFontSize or > AppSettings.MaximumVocabularyMeaningFontSize ||
            Display.WordMinutes is < 1 or > 720 || Todos.Length > 200 || Words.Length > 500 ||
            Todos.Any(t => t is null || string.IsNullOrWhiteSpace(t.Id) || t.Id.Length > 128 || !ValidText(t.Text, 160)) ||
            Todos.Select(t => t.Id).Distinct().Count() != Todos.Length ||
            Words.Any(w => w is null || !ValidText(w.Word, 80) || w.Meaning is null || w.Meaning.Length > 200))
            throw new InvalidDataException("同步数据格式或设置值有误。");
    }

    private static bool ValidText(string? text, int limit) => !string.IsNullOrWhiteSpace(text) && text.Length <= limit;

    public static string Hash(ReadOnlySpan<byte> snapshot) => Convert.ToHexString(SHA256.HashData(snapshot));

    public static CloudUploadDecision DecideUpload(string localHash, string? cloudHash, string? knownCloudHash)
    {
        if (cloudHash is null) return CloudUploadDecision.Upload;
        if (cloudHash == localHash) return CloudUploadDecision.AlreadyCurrent;
        return cloudHash == knownCloudHash ? CloudUploadDecision.Upload : CloudUploadDecision.CloudChanged;
    }

    public SyncSnapshot ClearLocalData(bool words, bool todos, bool display)
    {
        var defaults = display ? Capture(new AppSettings()) : null;
        return this with
        {
            Words = words ? [] : Words,
            Todos = todos ? [] : Todos,
            Appearance = defaults?.Appearance ?? Appearance,
            Display = defaults?.Display ?? Display
        };
    }
}
