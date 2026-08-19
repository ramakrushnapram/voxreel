namespace AIVIDEO.Server.Configuration;

/// <summary>
/// Local media storage. Pollo deletes generated media after 14 days, so every completed
/// asset is downloaded here and the local copy becomes the system of record.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Root directory for uploads and downloaded renders. Excluded from source control.</summary>
    public string Root { get; set; } = ".media";

    /// <summary>
    /// Publicly reachable origin for this server, e.g. a dev tunnel or ngrok URL.
    ///
    /// This matters more than it looks: Pollo's image-to-video endpoint takes an image *URL*
    /// and fetches it from their infrastructure. A localhost path is unreachable to them, so
    /// uploaded files can only be animated when this is set. Leave empty and the API will
    /// reject upload-based generation with an explanatory error rather than failing at Pollo.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    public int MaxUploadMb { get; set; } = 100;

    public bool HasPublicBaseUrl => !string.IsNullOrWhiteSpace(PublicBaseUrl);
}
