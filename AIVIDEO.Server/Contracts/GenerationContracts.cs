using System.ComponentModel.DataAnnotations;
using AIVIDEO.Server.Data.Entities;

namespace AIVIDEO.Server.Contracts;

/// <summary>Text prompt to a short clip (M3 Quick Clip).</summary>
public sealed record CreateTextToVideoRequest
{
    [Required, StringLength(2000, MinimumLength = 1)]
    public string Prompt { get; init; } = string.Empty;

    /// <summary>Seconds. Must be one of 4,5,6,7,8,9,10,11,12,15.</summary>
    public int Length { get; init; } = 5;

    public string Resolution { get; init; } = "720p";

    public string AspectRatio { get; init; } = "16:9";

    public string Mode { get; init; } = "basic";

    public bool GenerateAudio { get; init; }

    /// <summary>Model role: Hero or Broll. Resolved to a concrete endpoint by configuration.</summary>
    public string Role { get; init; } = "Broll";
}

/// <summary>Animate a still (M2). Supply exactly one of <see cref="ImageUrl"/> or <see cref="AssetId"/>.</summary>
public sealed record CreateImageToVideoRequest
{
    /// <summary>A publicly reachable JPG/PNG URL.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>
    /// A previously uploaded asset. Requires Storage:PublicBaseUrl to be configured, because
    /// Pollo fetches the image from their own infrastructure and cannot reach localhost.
    /// </summary>
    public Guid? AssetId { get; init; }

    [StringLength(2000)]
    public string? Prompt { get; init; }

    public int Length { get; init; } = 5;

    public string Resolution { get; init; } = "720p";

    public string Mode { get; init; } = "basic";

    public bool GenerateAudio { get; init; }
}

/// <summary>Still image generation and editing (M1 Image Studio).</summary>
public sealed record CreateImageRequest
{
    [Required, StringLength(10000, MinimumLength = 1)]
    public string Prompt { get; init; } = string.Empty;

    public string AspectRatio { get; init; } = "16:9";

    /// <summary>"1K" | "2K" | "4K".</summary>
    public string Resolution { get; init; } = "1K";

    /// <summary>Supplying either of these switches the model from generation into edit mode.</summary>
    public string? SourceImageUrl { get; init; }

    public Guid? SourceAssetId { get; init; }
}

public sealed record GenerationResponse
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required string Status { get; init; }

    public string? Model { get; init; }

    public string? Role { get; init; }

    public string? Prompt { get; init; }

    public string? SourceImageUrl { get; init; }

    public int? Length { get; init; }

    public string? Resolution { get; init; }

    public string? AspectRatio { get; init; }

    public string? FailMessage { get; init; }

    public decimal? CostUsd { get; init; }

    public string? PolloTaskId { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset? CompletedUtc { get; init; }

    public IReadOnlyList<AssetResponse> Assets { get; init; } = [];

    public static GenerationResponse From(GenerationRequest request) => new()
    {
        Id = request.Id,
        Kind = request.Kind.ToString(),
        Status = request.Status.ToString(),
        Model = request.Model,
        Role = request.Role,
        Prompt = request.Prompt,
        SourceImageUrl = request.SourceImageUrl,
        Length = request.Length,
        Resolution = request.Resolution,
        AspectRatio = request.AspectRatio,
        FailMessage = request.FailMessage,
        CostUsd = request.CostUsd,
        PolloTaskId = request.PolloTaskId,
        CreatedUtc = request.CreatedUtc,
        CompletedUtc = request.CompletedUtc,
        Assets = request.Assets.Select(AssetResponse.From).ToList()
    };
}

public sealed record AssetResponse
{
    public required Guid Id { get; init; }

    public required string Kind { get; init; }

    public required string ContentType { get; init; }

    public long Bytes { get; init; }

    /// <summary>Server-relative URL. Always use this rather than the expiring Pollo URL.</summary>
    public required string Url { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public static AssetResponse From(MediaAsset asset) => new()
    {
        Id = asset.Id,
        Kind = asset.Kind.ToString(),
        ContentType = asset.ContentType,
        Bytes = asset.Bytes,
        Url = $"/api/assets/{asset.Id}/raw",
        CreatedUtc = asset.CreatedUtc
    };
}

/// <summary>Startup diagnostics surfaced in the UI so misconfiguration is visible before a call fails.</summary>
public sealed record SystemStatusResponse
{
    public required bool PolloConfigured { get; init; }

    public required bool DatabaseReachable { get; init; }

    public required bool PublicBaseUrlConfigured { get; init; }

    public string? DatabaseError { get; init; }

    public required IReadOnlyDictionary<string, string> Models { get; init; }

    public required int[] AllowedClipLengths { get; init; }

    public required int MaxClipSeconds { get; init; }
}
