using System.ComponentModel.DataAnnotations;

namespace AIVIDEO.Server.Data.Entities;

/// <summary>
/// One unit of work sent to Pollo: a clip or a still. In the long-form pipeline a single
/// project produces roughly 150 of these, so this table is the hot path and the place
/// where cost and retry accounting lives.
/// </summary>
public class GenerationRequest
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public GenerationKind Kind { get; set; }

    /// <summary>Role the model was chosen for (Hero, Broll, ImageToVideo, Still).</summary>
    [MaxLength(64)]
    public string Role { get; set; } = string.Empty;

    /// <summary>Resolved "{brand}/{model}" path segment actually called.</summary>
    [MaxLength(128)]
    public string Model { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Prompt { get; set; }

    /// <summary>Public URL of the source image for image-to-video and image edits.</summary>
    [MaxLength(2048)]
    public string? SourceImageUrl { get; set; }

    public int? Length { get; set; }

    [MaxLength(16)]
    public string? Resolution { get; set; }

    [MaxLength(16)]
    public string? AspectRatio { get; set; }

    [MaxLength(16)]
    public string? Mode { get; set; }

    public bool GenerateAudio { get; set; }

    /// <summary>Exact JSON posted to Pollo. Kept for reproducing and diffing failures.</summary>
    public string RequestJson { get; set; } = string.Empty;

    /// <summary>Pollo's task identifier, null until submission succeeds.</summary>
    [MaxLength(128)]
    public string? PolloTaskId { get; set; }

    public GenerationStatus Status { get; set; } = GenerationStatus.Pending;

    [MaxLength(2000)]
    public string? FailMessage { get; set; }

    public decimal? CostUsd { get; set; }

    public decimal? Credit { get; set; }

    public int Attempts { get; set; }

    /// <summary>Next time the polling service should check this task. Spreads load across ticks.</summary>
    public DateTimeOffset? NextPollUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? SubmittedUtc { get; set; }

    public DateTimeOffset? CompletedUtc { get; set; }

    public ICollection<MediaAsset> Assets { get; set; } = new List<MediaAsset>();

    public bool IsTerminal =>
        Status is GenerationStatus.Succeeded or GenerationStatus.Failed or GenerationStatus.Cancelled;
}
