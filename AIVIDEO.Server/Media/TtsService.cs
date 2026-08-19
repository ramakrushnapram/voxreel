using System.Runtime.Versioning;
using System.Speech.Synthesis;

namespace AIVIDEO.Server.Media;

public interface ITtsService
{
    bool IsSupported { get; }

    /// <summary>Synthesizes narration to a WAV file and returns its duration.</summary>
    Task<TimeSpan> SynthesizeToWavAsync(string text, string wavPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Free, offline text-to-speech via the Windows Speech API (SAPI). No key, no cost, no network
/// — fitting the all-local theme. The voice is functional rather than lifelike; a later
/// provider (e.g. a cloud TTS) can implement this same interface for higher quality.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsTtsService(ILogger<WindowsTtsService> logger) : ITtsService
{
    public bool IsSupported => OperatingSystem.IsWindows();

    public Task<TimeSpan> SynthesizeToWavAsync(string text, string wavPath, CancellationToken cancellationToken = default)
    {
        // SAPI is synchronous and CPU-light; run it off the request thread so many scenes can
        // be synthesized without blocking the pipeline's async flow.
        return Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(wavPath)!);

            using var synth = new SpeechSynthesizer();
            synth.Rate = 0;      // -10..10; 0 is natural pace
            synth.Volume = 100;

            // Prefer a clearer installed voice when present, otherwise the system default.
            try
            {
                var voice = synth.GetInstalledVoices()
                    .FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en");
                if (voice is not null) synth.SelectVoice(voice.VoiceInfo.Name);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not select a specific voice; using the default.");
            }

            synth.SetOutputToWaveFile(wavPath);
            synth.Speak(text);
            synth.SetOutputToNull();

            // Duration is read back from the file rather than estimated, so the scene length
            // matches the audio exactly.
            var info = new FileInfo(wavPath);
            return EstimateWavDuration(wavPath, info.Length);
        }, cancellationToken);
    }

    /// <summary>
    /// Reads the duration from the WAV header (data-chunk bytes ÷ byte-rate). Falls back to a
    /// word-count estimate if the header can't be parsed, so a scene never ends up with zero length.
    /// </summary>
    private static TimeSpan EstimateWavDuration(string path, long fileLength)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path));
            reader.BaseStream.Seek(22, SeekOrigin.Begin);
            var channels = reader.ReadInt16();
            var sampleRate = reader.ReadInt32();
            reader.BaseStream.Seek(34, SeekOrigin.Begin);
            var bitsPerSample = reader.ReadInt16();

            // Find the "data" chunk.
            reader.BaseStream.Seek(12, SeekOrigin.Begin);
            while (reader.BaseStream.Position < reader.BaseStream.Length - 8)
            {
                var id = new string(reader.ReadChars(4));
                var size = reader.ReadInt32();
                if (id == "data")
                {
                    var byteRate = sampleRate * channels * (bitsPerSample / 8);
                    if (byteRate > 0)
                    {
                        return TimeSpan.FromSeconds((double)size / byteRate);
                    }
                    break;
                }
                reader.BaseStream.Seek(size, SeekOrigin.Current);
            }
        }
        catch
        {
            // fall through to estimate
        }

        return TimeSpan.FromSeconds(Math.Max(1.5, fileLength / 32000.0));
    }
}
