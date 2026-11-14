using Windows.Media.Control;

namespace Mizu.Widget;

public sealed record MediaSnapshot(string Title, string Artist, bool Playing, bool Previous, bool Toggle, bool Next, string Status)
{
    public static MediaSnapshot Empty(string status = "尚无系统媒体会话") => new("", "", false, false, false, false, status);
}

public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private readonly SemaphoreSlim gate = new(1);
    public async Task<MediaSnapshot> ReadAsync(CancellationToken cancel)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return MediaSnapshot.Empty("媒体控制需要 Windows 10 1809 或更新版本");
        await gate.WaitAsync(cancel);
        try
        {
            manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(cancel).WaitAsync(TimeSpan.FromSeconds(3), cancel);
            session = manager.GetCurrentSession();
            if (session is null) return MediaSnapshot.Empty();
            var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancel).WaitAsync(TimeSpan.FromSeconds(3), cancel);
            var playback = session.GetPlaybackInfo(); var controls = playback.Controls;
            bool playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            return new(properties.Title, properties.Artist, playing, controls.IsPreviousEnabled,
                controls.IsPlayPauseToggleEnabled || (playing ? controls.IsPauseEnabled : controls.IsPlayEnabled), controls.IsNextEnabled, session.SourceAppUserModelId);
        }
        finally { gate.Release(); }
    }
    public async Task<bool> CommandAsync(string command, CancellationToken cancel)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return false;
        await gate.WaitAsync(cancel);
        try
        {
            // Preserve the displayed session rather than sending global keystrokes to another app.
            var current = session; if (current is null) return false;
            var info = current.GetPlaybackInfo(); var c = info.Controls;
            var operation = command switch
            {
                "previous" when c.IsPreviousEnabled => current.TrySkipPreviousAsync(),
                "next" when c.IsNextEnabled => current.TrySkipNextAsync(),
                "toggle" when c.IsPlayPauseToggleEnabled => current.TryTogglePlayPauseAsync(),
                "toggle" when info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && c.IsPauseEnabled => current.TryPauseAsync(),
                "toggle" when c.IsPlayEnabled => current.TryPlayAsync(),
                _ => null
            };
            return operation is not null && await operation.AsTask(cancel).WaitAsync(TimeSpan.FromSeconds(3), cancel);
        }
        finally { gate.Release(); }
    }
}
