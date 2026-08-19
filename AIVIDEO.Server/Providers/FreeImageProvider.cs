using AIVIDEO.Server.Data.Entities;
using AIVIDEO.Server.Storage;

namespace AIVIDEO.Server.Providers;

/// <summary>
/// A no-key, no-cost image generator backed by Pollinations.ai.
///
/// Unlike Pollo (an async task API polled to completion), Pollinations serves the image
/// directly from a GET whose URL encodes the prompt. That makes this path synchronous: the
/// image is produced and downloaded within the request, so a free generation goes straight
/// to Succeeded with no polling. Generation can take 10–40s, hence the generous client
/// timeout configured in DI.
/// </summary>
public sealed class FreeImageProvider(
    HttpClient http,
    IAssetStore assetStore,
    ILogger<FreeImageProvider> logger)
{
    public const string ModelName = "pollinations/flux";

    /// <summary>
    /// Generates an image and returns the saved local asset (UserId set, not yet persisted to the DB).
    /// </summary>
    public async Task<MediaAsset> GenerateAsync(
        string prompt,
        string aspectRatio,
        string resolution,
        Guid userId,
        int seed,
        CancellationToken cancellationToken)
    {
        var (width, height) = Dimensions(aspectRatio, resolution);

        // nologo removes the watermark; a fixed model keeps output consistent; seed makes a
        // generation reproducible and, varied per request, avoids a cached image for a repeat
        // prompt; enhance lets the service expand a terse prompt into a richer one, which
        // noticeably lifts quality on short inputs.
        var url = $"https://image.pollinations.ai/prompt/{Uri.EscapeDataString(prompt)}" +
                  $"?width={width}&height={height}&nologo=true&enhance=true&model=flux&seed={seed}";

        logger.LogInformation("Requesting free image ({W}x{H}) from Pollinations.", width, height);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"The free image provider returned {(int)response.StatusCode}. Try again, or add a Pollo API key for a paid model.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";

        // Guard against an error page sneaking back as 200: a real result is an image.
        if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The free image provider returned {contentType} instead of an image. Try again in a moment.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var asset = await assetStore.SaveUploadAsync(stream, "image.jpg", contentType, AssetKind.Image, cancellationToken);
        asset.UserId = userId;
        return asset;
    }

    /// <summary>
    /// Maps an aspect ratio and a coarse resolution tier to pixel dimensions, with the long
    /// edge set by the tier. Capped at 1536 because the free service slows sharply and often
    /// times out at larger sizes — a deliberate ceiling, not the tier's nominal value.
    /// </summary>
    private static (int Width, int Height) Dimensions(string aspectRatio, string resolution)
    {
        // Higher tiers now request genuinely larger images for more detail. Capped at 2048:
        // beyond that the free service slows sharply and times out — a deliberate ceiling.
        var longEdge = resolution.ToUpperInvariant() switch
        {
            "2K" => 1536,
            "4K" => 2048,
            _ => 1024
        };

        var parts = aspectRatio.Split(':', 2);
        if (parts.Length == 2 &&
            double.TryParse(parts[0], out var w) && double.TryParse(parts[1], out var h) &&
            w > 0 && h > 0)
        {
            return w >= h
                ? (longEdge, RoundTo8(longEdge * h / w))
                : (RoundTo8(longEdge * w / h), longEdge);
        }

        return (longEdge, longEdge);
    }

    // Diffusion models expect dimensions that are multiples of 8.
    private static int RoundTo8(double value) => Math.Max(8, (int)Math.Round(value / 8) * 8);
}
