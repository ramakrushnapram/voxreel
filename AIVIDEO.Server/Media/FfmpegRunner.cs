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
    /// Renders a still into a clip of the given duration with a slow Ken Burns zoom, so a static
    /// image reads as motion under narration. Output is normalised (codec/fps/pixel format) so
    /// every scene concatenates cleanly.
    /// </summary>
    public async Task KenBurnsAsync(string imagePath, string outputPath, double seconds, int width, int height, CancellationToken cancellationToken)
    {
        var frames = Math.Max(1, (int)Math.Round(seconds * 25));
        // Upscale first so the zoom stays sharp, then zoompan, then pad to the exact target size.
        var vf =
            $"scale={width * 2}:{height * 2}," +
            $"zoompan=z='min(zoom+0.0008,1.3)':d={frames}:s={width}x{height}:fps=25," +
            "format=yuv420p";

        string[] args =
        [
            "-y", "-loop", "1", "-i", imagePath, "-t", seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "-vf", vf, "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p",
            outputPath
        ];

        await RunExpectingSuccessAsync(args, TimeSpan.FromMinutes(3), cancellationToken);
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
