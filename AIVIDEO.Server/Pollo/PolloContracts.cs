using System.Text.Json.Serialization;

namespace AIVIDEO.Server.Pollo;

/// <summary>
/// Envelope for every Pollo generation call. Verified against
/// https://docs.pollo.ai/m/pollo/pollo-v2-5 and .../google/nano-banana-pro.
/// </summary>
public sealed record PolloGenerationRequest
{
    [JsonPropertyName("input")]
    public required PolloInput Input { get; init; }

    [JsonPropertyName("webhookUrl")]
    public string? WebhookUrl { get; init; }

    [JsonPropertyName("clientSource")]
    public string? ClientSource { get; init; }
}

/// <summary>
/// Union of the input fields across Pollo models. Null members are omitted from the payload,
/// so one type serves text-to-video, image-to-video, and image generation without sending
/// fields a given model would reject.
/// </summary>
public sealed record PolloInput
{
    /// <summary>1–2000 chars for video models, up to 10000 for Nano Banana Pro.</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; init; }

    /// <summary>Image-to-video source. Must be a publicly reachable JPG/PNG/JPEG URL.</summary>
    [JsonPropertyName("image")]
    public string? Image { get; init; }

    /// <summary>Single-image edit source for image models (distinct from <see cref="Image"/>).</summary>
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    /// <summary>Multi-image edit sources for image models.</summary>
    [JsonPropertyName("images")]
    public IReadOnlyList<string>? Images { get; init; }

    /// <summary>Clip seconds. Allowed: 4,5,6,7,8,9,10,11,12,15. There is no longer option.</summary>
    [JsonPropertyName("length")]
    public int? Length { get; init; }

    /// <summary>Video: "720p"|"1080p". Images: "1K"|"2K"|"4K". Casing differs per model.</summary>
    [JsonPropertyName("resolution")]
    public string? Resolution { get; init; }

    [JsonPropertyName("aspectRatio")]
    public string? AspectRatio { get; init; }

    /// <summary>"basic" | "pro".</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; init; }

    /// <summary>
    /// Pollo's own ambient audio. Left false in the long-form pipeline because narration is
    /// produced separately and mixed by FFmpeg; generated audio would fight it.
    /// </summary>
    [JsonPropertyName("generateAudio")]
    public bool? GenerateAudio { get; init; }
}

public sealed record PolloTaskCreatedResponse
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }
}

public sealed record PolloTaskStatusResponse
{
    [JsonPropertyName("taskId")]
    public string? TaskId { get; init; }

    [JsonPropertyName("credit")]
    public decimal? Credit { get; init; }

    [JsonPropertyName("costUsd")]
    public decimal? CostUsd { get; init; }

    [JsonPropertyName("generations")]
    public List<PolloGeneration>? Generations { get; init; }
}

public sealed record PolloGeneration
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("failMsg")]
    public string? FailMsg { get; init; }

    /// <summary>The generated asset. Stops resolving 14 days after creation — download immediately.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("cover")]
    public string? Cover { get; init; }

    /// <summary>"image" | "video" | "text" | "audio".</summary>
    [JsonPropertyName("mediaType")]
    public string? MediaType { get; init; }
}

/// <summary>Pollo's task states, mirrored verbatim from the API.</summary>
public static class PolloStatus
{
    public const string Waiting = "waiting";
    public const string Processing = "processing";
    public const string Succeed = "succeed";
    public const string Failed = "failed";

    public static bool IsTerminal(string? status) =>
        string.Equals(status, Succeed, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, Failed, StringComparison.OrdinalIgnoreCase);

    public static bool IsSuccess(string? status) =>
        string.Equals(status, Succeed, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Allowed clip lengths. Enforced before submission so a bad value fails locally, not after a round trip.</summary>
public static class PolloLimits
{
    public static readonly int[] AllowedLengths = [4, 5, 6, 7, 8, 9, 10, 11, 12, 15];

    public const int MaxClipSeconds = 15;

    public static bool IsAllowedLength(int length) => Array.IndexOf(AllowedLengths, length) >= 0;
}

public sealed class PolloApiException(string message, int? statusCode = null, string? body = null)
    : Exception(message)
{
    public int? StatusCode { get; } = statusCode;

    public string? Body { get; } = body;
}
