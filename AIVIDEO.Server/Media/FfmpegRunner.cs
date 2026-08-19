using System.Diagnostics;

namespace AIVIDEO.Server.Media;

public sealed class FfmpegException(string message) : Exception(message);

/// <summary>
/// Thin wrapper around the ffmpeg binary. Resolves the executable from PATH or a configured
/// path, runs a command with a timeout, and fails loudly with captured stderr — ffmpeg puts
/// its real error there, so swallowing it would make failures undiagnosable.
/// </summary>
public sealed class FfmpegRunner(IConfiguration configuration, ILogger<FfmpegRunner> logger)
{
    private string FfmpegPath => Resolve("Ffmpeg:BinaryPath", "ffmpeg");

    public bool IsAvailable()
    {
        try
        {
            var (code, _, _) = RunAsync(["-version"], TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return code == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Renders a still into a clip with a Ken Burns move. The move varies by
    /// <paramref name="motionIndex"/> — zoom in, zoom out, and the four pan directions — so
    /// consecutive scenes don't all drift the same way, which is what made it feel static.
    /// Output is normalised (codec/fps/SAR/pixel format) so clips crossfade and concatenate cleanly.
    /// </summary>
    public async Task KenBurnsAsync(string imagePath, string outputPath, double seconds, int width, int height, int motionIndex, CancellationToken cancellationToken)
    {
        var frames = Math.Max(1, (int)Math.Round(seconds * 25));
        var f = frames.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // Centre helpers for zoom moves; pan moves override one axis. Expressions are in
        // zoompan's variables: on = output frame, iw/ih = (upscaled) input size, zoom = current z.
        const string cx = "iw/2-(iw/zoom/2)";
        const string cy = "ih/2-(ih/zoom/2)";

        // Bigger zoom range (up to 1.5) than before for a stronger sense of motion.
        var (z, x, y) = (motionIndex % 6) switch
        {
            0 => ($"min(1.0+0.5*on/{f},1.5)", cx, cy),                       // zoom in
            1 => ($"max(1.5-0.5*on/{f},1.0)", cx, cy),                       // zoom out
            2 => ("1.3", $"(iw-iw/zoom)*on/{f}", cy),                        // pan right
            3 => ("1.3", $"(iw-iw/zoom)*(1-on/{f})", cy),                    // pan left
            4 => ("1.3", cx, $"(ih-ih/zoom)*(1-on/{f})"),                    // pan up
            _ => ("1.3", cx, $"(ih-ih/zoom)*on/{f}"),                        // pan down
        };

        // Upscale 3x first so the zoom/pan stays sharp and jitter-free, then zoompan to target.
        var vf =
            $"scale={width * 3}:{height * 3}," +
            $"zoompan=z='{z}':x='{x}':y='{y}':d={frames}:s={width}x{height}:fps=25," +
            "setsar=1,format=yuv420p";

        string[] args =
        [
            "-y", "-loop", "1", "-i", imagePath, "-t", seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "-vf", vf, "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p",
            outputPath
        ];

        await RunExpectingSuccessAsync(args, TimeSpan.FromMinutes(3), cancellationToken);
    }

    /// <summary>
    /// Joins scene clips with crossfade transitions using the xfade filter. Each clip is expected
    /// to be rendered <paramref name="transitionSeconds"/> longer than its scene, so the overlaps
    /// don't eat into narration time — the final video ends up ~one transition longer than the
    /// narration, which the mux trims with -shortest.
    /// </summary>
    public async Task ConcatWithTransitionsAsync(
        IReadOnlyList<string> clipPaths,
        IReadOnlyList<double> clipSeconds,
        double transitionSeconds,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (clipPaths.Count == 1)
        {
            // Nothing to transition; just normalise-copy the single clip.
            string[] single = ["-y", "-i", clipPaths[0], "-c", "copy", outputPath];
            await RunExpectingSuccessAsync(single, TimeSpan.FromMinutes(3), cancellationToken);
            return;
        }

        var inputs = new List<string>();
        foreach (var p in clipPaths) { inputs.Add("-i"); inputs.Add(p); }

        // Build the xfade chain. Offset k = (length of the chain so far) - transition.
        var filter = new System.Text.StringBuilder();
        var t = transitionSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        var combined = clipSeconds[0];
        var last = "0:v";

        for (var i = 1; i < clipPaths.Count; i++)
        {
            var offset = Math.Max(0, combined - transitionSeconds);
            var label = i == clipPaths.Count - 1 ? "vout" : $"v{i}";
            filter.Append($"[{last}][{i}:v]xfade=transition=fade:duration={t}:")
                  .Append($"offset={offset.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}[{label}];");
            combined = combined + clipSeconds[i] - transitionSeconds;
            last = label;
        }

        var args = new List<string> { "-y" };
        args.AddRange(inputs);
        args.AddRange(["-filter_complex", filter.ToString().TrimEnd(';'),
            "-map", "[vout]", "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p",
            outputPath]);

        await RunExpectingSuccessAsync(args.ToArray(), TimeSpan.FromMinutes(8), cancellationToken);
    }

    /// <summary>Concatenates pre-normalised scene clips into one silent video track.</summary>
    public async Task ConcatAsync(IReadOnlyList<string> clipPaths, string listFilePath, string outputPath, CancellationToken cancellationToken)
    {
        // The concat demuxer reads a file listing the parts; safe with our generated paths.
        var lines = clipPaths.Select(p => $"file '{p.Replace("\\", "/").Replace("'", "'\\''")}'");
        await File.WriteAllLinesAsync(listFilePath, lines, cancellationToken);

        string[] args =
        [
            "-y", "-f", "concat", "-safe", "0", "-i", listFilePath,
            "-c", "copy", outputPath
        ];
        await RunExpectingSuccessAsync(args, TimeSpan.FromMinutes(5), cancellationToken);
    }

    /// <summary>Concatenates the per-scene narration WAVs into one audio track.</summary>
    public async Task ConcatAudioAsync(IReadOnlyList<string> wavPaths, string listFilePath, string outputPath, CancellationToken cancellationToken)
    {
        var lines = wavPaths.Select(p => $"file '{p.Replace("\\", "/").Replace("'", "'\\''")}'");
        await File.WriteAllLinesAsync(listFilePath, lines, cancellationToken);

        string[] args =
        [
            "-y", "-f", "concat", "-safe", "0", "-i", listFilePath,
            "-c:a", "pcm_s16le", outputPath
        ];
        await RunExpectingSuccessAsync(args, TimeSpan.FromMinutes(5), cancellationToken);
    }

    /// <summary>
    /// Synthesizes a soft ambient music bed of the given length — a low three-note chord pad
    /// with slow tremolo, a lowpass to take the edge off, and a touch of echo for space.
    /// Generated rather than sourced so there are no licensing concerns and no bundled files.
    /// </summary>
    public async Task GenerateMusicBedAsync(double seconds, string outputPath, CancellationToken cancellationToken)
    {
        var d = seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        // A minor-ish triad an octave-plus down: calm, unobtrusive, sits under speech.
        string[] args =
        [
            "-y",
            "-f", "lavfi", "-i", $"sine=frequency=130.81:duration={d}",
            "-f", "lavfi", "-i", $"sine=frequency=164.81:duration={d}",
            "-f", "lavfi", "-i", $"sine=frequency=196.00:duration={d}",
            "-filter_complex",
            "[0][1][2]amix=inputs=3:normalize=0,volume=0.12,tremolo=f=0.08:d=0.5,lowpass=f=750,aecho=0.8:0.7:60:0.35,afade=t=in:d=2,afade=t=out:st=" +
                (seconds - 2).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ":d=2[a]",
            "-map", "[a]", "-ar", "44100", "-ac", "2",
            outputPath
        ];
        await RunExpectingSuccessAsync(args, TimeSpan.FromMinutes(2), cancellationToken);
    }

    /// <summary>
    /// Mixes narration over a music bed with sidechain ducking: the music automatically drops
    /// while the voice is speaking and swells back in the gaps. Output is a single audio file.
    /// </summary>
    public async Task MixNarrationWithMusicAsync(string narrationPath, string musicPath, string outputPath, CancellationToken cancellationToken)
    {
        string[] args =
        [
            "-y", "-i", narrationPath, "-i", musicPath,
            "-filter_complex",
            // [1] music is keyed by [0] narration: louder voice -> more attenuation.
            "[1:a]volume=0.9[m];" +
            "[m][0:a]sidechaincompress=threshold=0.02:ratio=8:attack=20:release=500[duck];" +
            "[0:a][duck]amix=inputs=2:duration=first:normalize=0[a]",
            "-map", "[a]", "-c:a", "pcm_s16le", outputPath
        ];
        await RunExpectingSuccessAsync(args, TimeSpan.FromMinutes(3), cancellationToken);
    }

    /// <summary>Muxes the video track with the narration into the final MP4.</summary>
    public async Task MuxAsync(string videoPath, string audioPath, string outputPath, CancellationToken cancellationToken)
    {
        string[] args =
        [
            "-y", "-i", videoPath, "-i", audioPath,
            "-map", "0:v:0", "-map", "1:a:0",
            "-c:v", "copy", "-c:a", "aac", "-b:a", "192k",
            // End at the shorter stream so a rounding gap doesn't leave a frozen tail.
            "-shortest", outputPath
        ];
        await RunExpectingSuccessAsync(args, TimeSpan.FromMinutes(5), cancellationToken);
    }

    private async Task RunExpectingSuccessAsync(string[] args, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var (code, _, stderr) = await RunAsync(args, timeout, cancellationToken);
        if (code != 0)
        {
            var tail = stderr.Length > 800 ? stderr[^800..] : stderr;
            throw new FfmpegException($"ffmpeg exited {code}. {tail}");
        }
    }

    private async Task<(int Code, string StdOut, string StdErr)> RunAsync(string[] args, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = FfmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
        {
            throw new FfmpegException($"Could not start ffmpeg at '{FfmpegPath}'. Is it installed and on PATH?");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { /* already gone */ }
            throw new FfmpegException($"ffmpeg timed out after {timeout.TotalSeconds:0}s.");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private string Resolve(string configKey, string fallback)
    {
        var configured = configuration[configKey];
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }
}
