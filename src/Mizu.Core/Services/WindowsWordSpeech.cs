using System.Globalization;
using System.Runtime.InteropServices;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Mizu.Core.Services;

public sealed class WindowsWordSpeech : IDisposable
{
    private readonly Lazy<VoiceInformation?> voice = new(FindEnglishVoice);
    private readonly Lazy<DesktopVoice?> desktop = new(DesktopVoice.Open);
    private CancellationTokenSource? active;
    private bool disposed;
    public string? VoiceName => disposed ? null : voice.Value?.DisplayName ?? desktop.Value?.Name;

    private static VoiceInformation? FindEnglishVoice()
    {
        try
        {
            static bool IsEnglish(VoiceInformation? voice) =>
                voice?.Language.StartsWith("en-", StringComparison.OrdinalIgnoreCase) == true;
            var preferred = SpeechSynthesizer.DefaultVoice;
            if (IsEnglish(preferred)) return preferred;
            var voices = SpeechSynthesizer.AllVoices;
            return voices.FirstOrDefault(v => v.Language.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                ?? voices.FirstOrDefault(IsEnglish);
        }
        catch (Exception ex)
        {
            AppLog.Write("vocabulary.voice", ex.GetType().Name);
            return null;
        }
    }

    public async Task<bool> PlayAsync(string word)
    {
        if (disposed || !EnglishLexicon.Contains(word) || VoiceName is null) return false;
        active?.Cancel();
        using var playback = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        active = playback;
        try
        {
            if (voice.Value is not { } english)
                return await desktop.Value!.PlayAsync(word.Trim(), playback.Token);

            using var synth = new SpeechSynthesizer { Voice = english };
            using var stream = await synth.SynthesizeTextToStreamAsync(word.Trim()).AsTask(playback.Token);
            playback.Token.ThrowIfCancellationRequested();
            using var source = MediaSource.CreateFromStream(stream, stream.ContentType);
            using var player = new MediaPlayer();
            player.CommandManager.IsEnabled = false;
            player.SystemMediaTransportControls.IsEnabled = false;
            var end = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            player.MediaEnded += (_, _) => end.TrySetResult(true);
            player.MediaFailed += (_, _) => end.TrySetResult(false);
            player.Source = source;
            player.Play();
            return await end.Task.WaitAsync(playback.Token);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            AppLog.Write("vocabulary.play", ex.GetType().Name);
            return false;
        }
        finally
        {
            if (ReferenceEquals(active, playback))
            {
                if (!disposed && playback.IsCancellationRequested && desktop.IsValueCreated) desktop.Value?.Stop();
                active = null;
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        active?.Cancel();
        if (desktop.IsValueCreated) desktop.Value?.Dispose();
    }

    private sealed class DesktopVoice(object speaker, string name) : IDisposable
    {
        private const int SpeakAsyncAndReplace = 1 | 2 | 16;
        public string Name => name;

        public static DesktopVoice? Open()
        {
            object? instance = null, collection = null;
            try
            {
                var type = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (type is null) return null;
                instance = Activator.CreateInstance(type);
                if (instance is null) return null;
                dynamic voice = instance;
                collection = voice.GetVoices("", "Language=409");
                dynamic tokens = collection;
                for (int i = 0; i < (int)tokens.Count; i++)
                {
                    object token = tokens.Item(i);
                    try
                    {
                        dynamic candidate = token;
                        string languages = candidate.GetAttribute("Language");
                        if (!languages.Split(';').Any(language =>
                            int.TryParse(language, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int id) && (id & 0x3ff) == 9))
                            continue;
                        voice.Voice = candidate;
                        string description = candidate.GetDescription(0);
                        var result = new DesktopVoice(instance, description);
                        instance = null;
                        return result;
                    }
                    finally { Release(token); }
                }
                return null;
            }
            catch (Exception ex)
            {
                AppLog.Write("vocabulary.desktopVoice", ex.GetType().Name);
                return null;
            }
            finally { Release(collection); Release(instance); }
        }

        public async Task<bool> PlayAsync(string text, CancellationToken token)
        {
            dynamic voice = speaker;
            voice.Speak(text, SpeakAsyncAndReplace);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if ((bool)voice.WaitUntilDone(0)) return true;
                await Task.Delay(40, token);
            }
        }

        public void Stop()
        {
            try { ((dynamic)speaker).Speak("", SpeakAsyncAndReplace); }
            catch (Exception ex) { AppLog.Write("vocabulary.desktopStop", ex.GetType().Name); }
        }

        public void Dispose() { Stop(); Release(speaker); }

        private static void Release(object? instance)
        {
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
        }
    }
}
