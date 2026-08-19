using System.ComponentModel.DataAnnotations;

namespace AIVIDEO.Server.Data.Entities;

/// <summary>
/// A file on local disk. Covers both user uploads and downloaded Pollo output.
///
/// <see cref="RelativePath"/> is the system of record. <see cref="SourceUrl"/> is kept only
/// for provenance and debugging — it points at Pollo's CDN and stops resolving after
/// <see cref="RemoteExpiresUtc"/>, so nothing may depend on it.
/// </summary>
public class MediaAsset
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owner. Set directly for uploads (which have no generation) and copied from the generation otherwise.</summary>
    public Guid UserId { get; set; }

    public Guid? GenerationRequestId { get; set; }

    public GenerationRequest? GenerationRequest { get; set; }

    public AssetKind Kind { get; set; }

    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(128)]
    public string ContentType { get; set; } = "application/octet-stream";

    /// <summary>Path relative to StorageOptions.Root. Never an absolute path — the root moves between environments.</summary>
    [MaxLength(512)]
    public string RelativePath { get; set; } = string.Empty;

    public long Bytes { get; set; }

    [MaxLength(64)]
    public string? Sha256 { get; set; }

    [MaxLength(2048)]
    public string? SourceUrl { get; set; }

    /// <summary>When the Pollo URL stops working. Informational: the local copy is authoritative.</summary>
    public DateTimeOffset? RemoteExpiresUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
