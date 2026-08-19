using AIVIDEO.Server.Data.Entities;

namespace AIVIDEO.Server.Storage;

public interface IAssetStore
{
    /// <summary>Persists an uploaded file and returns the tracked asset (not yet saved to the database).</summary>
    Task<MediaAsset> SaveUploadAsync(
        Stream content,
        string fileName,
        string contentType,
        AssetKind kind,
        CancellationToken cancellationToken = default);

    /// <summary>Streams a remote URL to disk. Used to capture Pollo output before its 14-day expiry.</summary>
    Task<MediaAsset> SaveFromUrlAsync(
        string url,
        AssetKind kind,
        Guid? generationRequestId,
        CancellationToken cancellationToken = default);

    /// <summary>Absolute path for serving. Null when the file is missing from disk.</summary>
    string? ResolvePath(MediaAsset asset);

    /// <summary>Publicly reachable URL for this asset, or null when Storage:PublicBaseUrl is unset.</summary>
    string? BuildPublicUrl(MediaAsset asset);
}
