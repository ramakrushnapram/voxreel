namespace AIVIDEO.Server.Data.Entities;

public enum GenerationKind
{
    TextToVideo = 0,
    ImageToVideo = 1,
    Image = 2,
    ImageEdit = 3
}

/// <summary>
/// Lifecycle of one generation. Distinct from Pollo's own status because we add local
/// stages either side of theirs: Pending (not yet submitted) and Downloading (Pollo
/// succeeded, we are pulling the asset local before its 14-day URL expires).
/// </summary>
public enum GenerationStatus
{
    Pending = 0,
    Submitted = 1,
    Processing = 2,
    Downloading = 3,
    Succeeded = 4,
    Failed = 5,
    Cancelled = 6
}

public enum AssetKind
{
    SourceImage = 0,
    Video = 1,
    Image = 2,
    Thumbnail = 3,
    Audio = 4
}
