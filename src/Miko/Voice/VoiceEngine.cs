using System;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Miko.Security;

namespace Miko.Voice;

public sealed class VoiceEngine : IDisposable
{
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    private readonly AppConfig _config;
    private CancellationTokenSource? _playbackCts;
    private dynamic? _activeWmpPlayer;
    private SpeechSynthesizer? _activeSynthesizer;
    private bool _isDisposed = false;

    public bool IsMuted { get; set; } = false;
    public bool IsSpeaking { get; private set; } = false;

    public event Action<double>? PlaybackStarted;
    public event Action? PlaybackFinished;

    public VoiceEngine(AppConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Toggles the voice mute state. When muted, any currently playing voice audio is stopped immediately.
    /// </summary>
    public void ToggleMute()
    {
        IsMuted = !IsMuted;
        if (IsMuted)
        {
            Stop();
        }
    }

    /// <summary>
    /// Speaks the reply text out loud asynchronously.
    /// Tries Fish Audio if configured and available; falls back automatically to Windows SpeechSynthesizer.
    /// </summary>
    public async Task SpeakAsync(string replyText, CancellationToken externalCt = default)
    {
        if (_isDisposed || !_config.VoiceEnabled || IsMuted) return;
        if (string.IsNullOrWhiteSpace(replyText)) return;

        Stop();

        _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _playbackCts.Token;

        try
        {
            IsSpeaking = true;

            bool success = false;
            if (string.Equals(_config.VoiceProvider, "fishaudio", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(_config.FishApiKey))
            {
                try
                {
                    success = await TryFishAudioAsync(replyText, ct);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[VoiceEngine] TryFishAudioAsync threw: {ex.Message}");
                    success = false;
                }
            }

            if (!success && !ct.IsCancellationRequested &&
                string.Equals(_config.FallbackVoiceProvider, "windows", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Run(() => SpeakWindowsFallback(replyText, ct), ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when user cancels or speaks new prompt
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VoiceEngine Error] {SecurityGuard.Sanitize(ex.Message, _config.FishApiKey)}");
        }
        finally
        {
            IsSpeaking = false;
            PlaybackFinished?.Invoke();
        }
    }

    /// <summary>
    /// Immediately halts any playing audio or active Windows TTS speech.
    /// </summary>
    public void Stop()
    {
        try
        {
            _playbackCts?.Cancel();
            _playbackCts?.Dispose();
            _playbackCts = null;

            if (_activeWmpPlayer != null)
            {
                try { _activeWmpPlayer.controls.stop(); } catch { }
                try { _activeWmpPlayer.close(); } catch { }
                _activeWmpPlayer = null;
            }

            if (_activeSynthesizer != null)
            {
                _activeSynthesizer.SpeakAsyncCancelAll();
                _activeSynthesizer = null;
            }
        }
        catch { }
    }

    /// <summary>
    /// Cleans Markdown formatting (code blocks, backticks, asterisks, headers) while preserving
    /// square-bracket emotion tags like [laughing], [giggle], [sigh], [excited].
    /// </summary>
    public static string CleanMarkdownForFishAudio(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // 1. Remove fenced code blocks ```...```
        string text = Regex.Replace(input, @"```[\s\S]*?```", "");

        // 2. Remove inline code `...`
        text = Regex.Replace(text, @"`([^`]+)`", "$1");

        // 3. Remove Markdown headings (#, ##, etc.)
        text = Regex.Replace(text, @"^[ \t]*#+[ \t]*", "", RegexOptions.Multiline);

        // 4. Remove bold / italic markers (**bold**, *italic*, __bold__, _italic_)
        text = Regex.Replace(text, @"(\*\*|__)(.*?)\1", "$2");
        text = Regex.Replace(text, @"(\*|_)(.*?)\1", "$2");
        text = Regex.Replace(text, @"~~(.*?)~~", "$2");

        // 5. Remove Markdown links [text](url) -> preserve link text (only when followed by (url))
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");

        // 6. Remove blockquotes and bullet points
        text = Regex.Replace(text, @"^[ \t]*>[ \t]*", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"^[ \t]*[-*+][ \t]+", "", RegexOptions.Multiline);

        // 7. Collapse whitespace and trim
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    /// <summary>
    /// Strips [bracketed] emotion cues so Windows offline TTS doesn't read them out literally.
    /// </summary>
    public static string StripEmotionTags(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        string stripped = Regex.Replace(input, @"\[[a-zA-Z0-9_\-\s]+\]", "");
        return Regex.Replace(stripped, @"\s+", " ").Trim();
    }

    private async Task<bool> TryFishAudioAsync(string rawText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_config.FishApiKey)) return false;

        string spokenText = CleanMarkdownForFishAudio(rawText);
        if (string.IsNullOrWhiteSpace(spokenText)) return false;

        const string endpoint = "https://api.fish.audio/v1/tts";
        SecurityGuard.ValidateOutboundEndpoint(endpoint);

        string voiceId = string.IsNullOrWhiteSpace(_config.FishVoiceId)
            ? "fdef76c9b2e741119d817754bd8b5dd0"
            : _config.FishVoiceId;

        var payload = new
        {
            text = spokenText,
            reference_id = voiceId,
            format = "mp3"
        };

        string jsonPayload = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.FishApiKey);
        if (!string.IsNullOrWhiteSpace(_config.FishModel))
        {
            request.Headers.TryAddWithoutValidation("model", _config.FishModel);
        }
        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                string errBody = await response.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[VoiceEngine] Fish Audio HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {SecurityGuard.Sanitize(errBody, _config.FishApiKey)}");
            }
            catch { }
            return false;
        }

        byte[] audioData = await response.Content.ReadAsByteArrayAsync(ct);
        if (audioData == null || audioData.Length == 0) return false;

        Console.WriteLine($"[VoiceEngine] Fish Audio success: playing Capri MP3 ({audioData.Length} bytes)...");
        return await PlayMp3BytesAsync(audioData, ct);
    }

    private async Task<bool> PlayMp3BytesAsync(byte[] mp3Bytes, CancellationToken ct)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Miko");
        Directory.CreateDirectory(tempDir);
        string tempFile = Path.Combine(tempDir, $"speech_{Guid.NewGuid():N}.mp3");

        try
        {
            await File.WriteAllBytesAsync(tempFile, mp3Bytes, ct);

            Type? wmpType = Type.GetTypeFromProgID("WMPlayer.OCX");
            if (wmpType == null)
            {
                Console.WriteLine("[VoiceEngine] Windows Media Player COM component not found.");
                return false;
            }

            dynamic player = Activator.CreateInstance(wmpType)!;
            _activeWmpPlayer = player;

            player.settings.autoStart = true;
            player.settings.volume = 100;
            player.URL = tempFile;

            // Give WMP a moment to start opening the stream
            await Task.Delay(150, ct);

            // Wait for playback to begin (timeout after 3s)
            int waitStart = 0;
            bool started = false;
            while (!ct.IsCancellationRequested && waitStart < 30)
            {
                int state = GetSafePlayState(player);
                if (state == 3) // Playing
                {
                    started = true;
                    break;
                }
                if (state == 8 && waitStart > 2) // MediaEnded after having started
                {
                    started = true;
                    break;
                }
                await Task.Delay(100, ct);
                waitStart++;
            }

            if (started)
            {
                double duration = GetSafeMediaDuration(player);
                if (duration <= 0)
                {
                    duration = Math.Max(1.0, mp3Bytes.Length / 16000.0);
                }

                PlaybackStarted?.Invoke(duration);
            }
            else
            {
                try { player.controls.stop(); } catch { }
                try { player.close(); } catch { }
                return false;
            }

            // Wait until playback completes
            while (!ct.IsCancellationRequested)
            {
                int state = GetSafePlayState(player);
                // 3: Playing, 6: Buffering, 7: Waiting, 9: Transitioning, 0: Undefined, 10: Ready
                if (state != 3 && state != 6 && state != 7 && state != 9 && state != 0 && state != 10)
                {
                    break;
                }
                await Task.Delay(100, ct);
            }

            try { player.controls.stop(); } catch { }
            try { player.close(); } catch { }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VoiceEngine WMP error]: {ex.Message}");
            return false;
        }
        finally
        {
            _activeWmpPlayer = null;
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    private static double GetSafeMediaDuration(dynamic player)
    {
        for (int i = 0; i < 6; i++)
        {
            try
            {
                if (player.currentMedia != null)
                {
                    double d = (double)player.currentMedia.duration;
                    if (d > 0) return d;
                }
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(50);
            }
            catch
            {
                return 0.0;
            }
        }
        return 0.0;
    }

    private static int GetSafePlayState(dynamic player)
    {
        for (int i = 0; i < 8; i++)
        {
            try
            {
                return (int)player.playState;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Thread.Sleep(50);
            }
            catch
            {
                return -1;
            }
        }
        return -1;
    }

    private void SpeakWindowsFallback(string rawText, CancellationToken ct)
    {
        string plainText = StripEmotionTags(CleanMarkdownForFishAudio(rawText));
        if (string.IsNullOrWhiteSpace(plainText)) return;

        Console.WriteLine($"[VoiceEngine] Fallback: Speaking using offline Windows TTS (SpeechSynthesizer)...");

        try
        {
            using var synth = new SpeechSynthesizer();
            synth.SetOutputToDefaultAudioDevice();
            _activeSynthesizer = synth;

            using var reg = ct.Register(() =>
            {
                try { synth.SpeakAsyncCancelAll(); } catch { }
            });

            // Estimated duration for Windows TTS (~14 characters per second)
            double estimatedDuration = Math.Max(1.0, plainText.Length / 14.0);
            PlaybackStarted?.Invoke(estimatedDuration);

            synth.Speak(plainText);
        }
        catch
        {
            // Silent fallback handling
        }
        finally
        {
            _activeSynthesizer = null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
    }
}
