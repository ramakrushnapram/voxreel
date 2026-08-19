using System.Text.RegularExpressions;
using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Data.Entities;
using AIVIDEO.Server.Llm;
using AIVIDEO.Server.Providers;
using AIVIDEO.Server.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIVIDEO.Server.Media;

/// <summary>
/// Builds a long-form video end to end: script → scenes → per-scene narration and visuals →
/// FFmpeg assembly into one MP4. Runs as a background job (see <see cref="LongFormRunner"/>)
/// because a full build is minutes of work and hundreds of operations.
///
/// The narration is the timeline: each scene's on-screen duration comes from the measured
/// length of its synthesized audio, so picture and voice stay in sync.
/// </summary>
public sealed partial class LongFormService(
    AppDbContext db,
    LlmService llm,
    IOllamaClient ollama,
    FreeImageProvider freeImage,
    IAssetStore assetStore,
    ITtsService tts,
    FfmpegRunner ffmpeg,
    IOptionsMonitor<StorageOptions> storageOptions,
    ILogger<LongFormService> logger)
{
    public async Task RunAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await db.VideoProjects
            .Include(p => p.Scenes)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
        {
            logger.LogWarning("Long-form project {Id} vanished before the pipeline ran.", projectId);
            return;
        }

        try
        {
            EnsureDependencies();

            await PlanAsync(project, cancellationToken);
            await NarrateAsync(project, cancellationToken);
            await VisualsAsync(project, cancellationToken);
            await AssembleAsync(project, cancellationToken);

            project.Status = VideoProjectStatus.Ready;
            project.Progress = 100;
            await SaveAsync(project, cancellationToken);
            logger.LogInformation("Long-form project {Id} completed.", projectId);
        }
        catch (Exception ex)
        {
            project.Status = VideoProjectStatus.Failed;
            project.ErrorMessage = ex.Message;
            await SaveAsync(project, cancellationToken);
            logger.LogError(ex, "Long-form project {Id} failed.", projectId);
        }
    }

    // ---- Stage 1: plan ----
    private async Task PlanAsync(VideoProject project, CancellationToken cancellationToken)
    {
        project.Status = VideoProjectStatus.Planning;
        project.Progress = 5;
        await SaveAsync(project, cancellationToken);

        if (string.IsNullOrWhiteSpace(project.ScriptText))
        {
            var scriptResult = await llm.GenerateScriptAsync(
                project.UserId, project.Topic, project.TargetMinutes, project.UseRag, cancellationToken);
            project.ScriptText = scriptResult.Script;
        }

        var sentences = SplitSentences(project.ScriptText);
        // Group sentences into scenes of ~2 so each is a handful of seconds of narration.
        var scenes = new List<Scene>();
        for (var i = 0; i < sentences.Count; i += 2)
        {
            var text = string.Join(" ", sentences.Skip(i).Take(2)).Trim();
            if (text.Length == 0) continue;
            scenes.Add(new Scene
            {
                VideoProjectId = project.Id,
                UserId = project.UserId,
                Index = scenes.Count,
                NarrationText = text
            });
        }

        if (scenes.Count == 0)
        {
            throw new InvalidOperationException("The script produced no usable scenes.");
        }

        db.Scenes.AddRange(scenes);
        project.Progress = 20;
        await SaveAsync(project, cancellationToken);

        project.Scenes = scenes;
    }

    // ---- Stage 2: narrate (sets each scene's duration) ----
    private async Task NarrateAsync(VideoProject project, CancellationToken cancellationToken)
    {
        project.Status = VideoProjectStatus.Narrating;
        await SaveAsync(project, cancellationToken);

        var workDir = WorkDir(project.Id);
        var count = project.Scenes.Count;
        var done = 0;

        foreach (var scene in project.Scenes.OrderBy(s => s.Index))
        {
            var wavPath = Path.Combine(workDir, $"scene_{scene.Index:D4}.wav");
            var duration = await tts.SynthesizeToWavAsync(scene.NarrationText, wavPath, cancellationToken);
            scene.DurationMs = (int)Math.Max(1000, duration.TotalMilliseconds);

            done++;
            project.Progress = 20 + (int)(20.0 * done / count);
            await SaveAsync(project, cancellationToken);
        }
    }

    // ---- Stage 3: per-scene visuals ----
    private async Task VisualsAsync(VideoProject project, CancellationToken cancellationToken)
    {
        project.Status = VideoProjectStatus.GeneratingVisuals;
        await SaveAsync(project, cancellationToken);

        var count = project.Scenes.Count;
        var done = 0;

        foreach (var scene in project.Scenes.OrderBy(s => s.Index))
        {
            scene.VisualPrompt = await DescribeVisualAsync(scene.NarrationText, cancellationToken);

            // Free provider only, for now — a long video at Pollo image rates would be costly.
            // Generate at 2K so there's real detail to zoom into without the picture going soft.
            var seed = (int)(scene.Id.GetHashCode() & 0x7fffffff);
            var asset = await freeImage.GenerateAsync(
                scene.VisualPrompt, project.AspectRatio, "2K", project.UserId, seed, cancellationToken);
            asset.GenerationRequestId = null;
            db.MediaAssets.Add(asset);
            scene.ImageAssetId = asset.Id;

            done++;
            project.Progress = 40 + (int)(40.0 * done / count);
            await SaveAsync(project, cancellationToken);
        }
    }

    // ---- Stage 4: assemble with FFmpeg ----
    private async Task AssembleAsync(VideoProject project, CancellationToken cancellationToken)
    {
        project.Status = VideoProjectStatus.Assembling;
        project.Progress = 82;
        await SaveAsync(project, cancellationToken);

        var workDir = WorkDir(project.Id);
        var (width, height) = Dimensions(project.AspectRatio);

        // Crossfade duration. Each clip is rendered this much longer than its scene so the
        // overlaps don't steal narration time; keep it short so brief scenes still work.
        const double transition = 0.5;

        var clipPaths = new List<string>();
        var clipSeconds = new List<double>();
        var wavPaths = new List<string>();

        foreach (var scene in project.Scenes.OrderBy(s => s.Index))
        {
            var image = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == scene.ImageAssetId, cancellationToken);
            var imagePath = image is null ? null : assetStore.ResolvePath(image);
            var wavPath = Path.Combine(workDir, $"scene_{scene.Index:D4}.wav");
            var clipPath = Path.Combine(workDir, $"clip_{scene.Index:D4}.mp4");

            if (imagePath is null || !File.Exists(imagePath) || !File.Exists(wavPath))
            {
                logger.LogWarning("Skipping scene {Index} — missing image or audio.", scene.Index);
                continue;
            }

            var sceneSeconds = Math.Max(1.0, scene.DurationMs / 1000.0);
            var clipLength = sceneSeconds + transition;   // extra tail absorbed by the crossfade
            // Vary the motion per scene so consecutive shots don't all move the same way.
            await ffmpeg.KenBurnsAsync(imagePath, clipPath, clipLength, width, height, scene.Index, cancellationToken);
            clipPaths.Add(clipPath);
            clipSeconds.Add(clipLength);
            wavPaths.Add(wavPath);
        }

        if (clipPaths.Count == 0)
        {
            throw new InvalidOperationException("No scenes could be rendered.");
        }

        var videoTrack = Path.Combine(workDir, "video.mp4");
        var narrationTrack = Path.Combine(workDir, "narration.wav");
        var finalPath = Path.Combine(workDir, "final.mp4");

        // If subtitles are on, the burn step re-encodes this track — so render it fast here and
        // let that pass set final quality. If not, this track is final, so render it at quality.
        await ffmpeg.ConcatWithTransitionsAsync(
            clipPaths, clipSeconds, transition, videoTrack, fastIntermediate: project.Subtitles, cancellationToken);
        await ffmpeg.ConcatAudioAsync(wavPaths, Path.Combine(workDir, "audio.txt"), narrationTrack, cancellationToken);

        // Optionally lay a synthesized ambient bed under the narration (ducked when the voice
        // plays). Falls back to narration-only if music generation fails — music is a nicety,
        // not worth failing the whole render over.
        var finalAudioTrack = narrationTrack;
        if (project.BackgroundMusic)
        {
            try
            {
                var totalSeconds = Math.Max(2.0, project.Scenes.Sum(s => s.DurationMs) / 1000.0);
                var musicPath = Path.Combine(workDir, "music.wav");
                var mixedPath = Path.Combine(workDir, "mixed.wav");
                await ffmpeg.GenerateMusicBedAsync(totalSeconds, musicPath, cancellationToken);
                await ffmpeg.MixNarrationWithMusicAsync(narrationTrack, musicPath, mixedPath, cancellationToken);
                finalAudioTrack = mixedPath;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Background music failed for {Id}; using narration only.", project.Id);
            }
        }

        // Burn captions when enabled: timings follow the sequential narration (cumulative scene
        // durations), which is what the audio track uses — the crossfades only shift the picture.
        if (project.Subtitles)
        {
            try
            {
                var srtPath = Path.Combine(workDir, "captions.srt");
                await File.WriteAllTextAsync(srtPath, BuildSrt(project.Scenes.OrderBy(s => s.Index)), cancellationToken);
                await ffmpeg.BurnSubtitlesAndMuxAsync(videoTrack, finalAudioTrack, srtPath, finalPath, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Subtitle burn failed for {Id}; muxing without captions.", project.Id);
                await ffmpeg.MuxAsync(videoTrack, finalAudioTrack, finalPath, cancellationToken);
            }
        }
        else
        {
            await ffmpeg.MuxAsync(videoTrack, finalAudioTrack, finalPath, cancellationToken);
        }

        // Register the finished MP4 as an asset the user can stream and download.
        await using var stream = File.OpenRead(finalPath);
        var outputAsset = await assetStore.SaveUploadAsync(stream, "video.mp4", "video/mp4", AssetKind.Video, cancellationToken);
        outputAsset.UserId = project.UserId;
        db.MediaAssets.Add(outputAsset);

        project.OutputAssetId = outputAsset.Id;
        project.Progress = 98;
        await SaveAsync(project, cancellationToken);
    }

    /// <summary>Turns a line of narration into a one-line concrete image prompt; falls back to the narration itself.</summary>
    private async Task<string> DescribeVisualAsync(string narration, CancellationToken cancellationToken)
    {
        try
        {
            const string system =
                "Given a line of narration, describe ONE photographic image that illustrates it. " +
                "Reply with a single vivid image description — no preamble, no quotes. Favour concrete, " +
                "filmable subjects and settings over abstract concepts.";
            var result = await ollama.GenerateAsync(system, narration, cancellationToken);
            var line = result.Trim().Trim('"');
            return line.Length is > 5 and < 500 ? line : narration;
        }
        catch
        {
            return narration;
        }
    }

    private void EnsureDependencies()
    {
        if (!tts.IsSupported)
        {
            throw new InvalidOperationException("Text-to-speech (Windows SAPI) is not available on this platform.");
        }
        if (!ffmpeg.IsAvailable())
        {
            throw new InvalidOperationException(
                "FFmpeg is not installed or not on PATH. Install it (e.g. `winget install Gyan.FFmpeg`) and restart the server.");
        }
    }

    private string WorkDir(Guid projectId)
    {
        var dir = Path.Combine(Path.GetFullPath(storageOptions.CurrentValue.Root), "work", projectId.ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task SaveAsync(VideoProject project, CancellationToken cancellationToken)
    {
        project.UpdatedUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    // 1080p output: 720p looked soft, especially once scaled up in a browser player.
    private static (int Width, int Height) Dimensions(string aspectRatio) => aspectRatio switch
    {
        "9:16" => (1080, 1920),
        "1:1" => (1080, 1080),
        _ => (1920, 1080)
    };

    /// <summary>
    /// Builds an SRT from the scenes, timing each caption to its narration span (cumulative
    /// durations). Long narration is wrapped to two lines so captions don't run off-screen.
    /// </summary>
    private static string BuildSrt(IEnumerable<Scene> scenes)
    {
        var sb = new System.Text.StringBuilder();
        var cursor = TimeSpan.Zero;
        var n = 1;
        foreach (var scene in scenes)
        {
            var start = cursor;
            var end = cursor + TimeSpan.FromMilliseconds(Math.Max(1000, scene.DurationMs));
            sb.Append(n++).Append('\n')
              .Append(Srt(start)).Append(" --> ").Append(Srt(end)).Append('\n')
              .Append(Wrap(scene.NarrationText)).Append("\n\n");
            cursor = end;
        }
        return sb.ToString();
    }

    private static string Srt(TimeSpan t) =>
        $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2},{t.Milliseconds:D3}";

    /// <summary>Wraps caption text to roughly two lines so it stays on screen.</summary>
    private static string Wrap(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var line = new System.Text.StringBuilder();
        foreach (var w in words)
        {
            if (line.Length + w.Length > 42 && line.Length > 0)
            {
                lines.Add(line.ToString());
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(w);
        }
        if (line.Length > 0) lines.Add(line.ToString());
        // Keep at most two lines; anything longer is a sign the scene grouping is too big.
        return string.Join("\n", lines.Take(2));
    }

    private static List<string> SplitSentences(string text)
    {
        // Split on sentence-ending punctuation followed by whitespace; keeps the punctuation.
        return SentenceRegex().Split(text.Replace("\r\n", " ").Replace("\n", " "))
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceRegex();
}
