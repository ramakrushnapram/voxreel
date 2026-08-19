using System.Security.Cryptography;
using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Data.Entities;
using AIVIDEO.Server.Pollo;
using Microsoft.Extensions.Options;

namespace AIVIDEO.Server.Storage;

/// <summary>
/// Filesystem-backed asset store. Files are foldered by UTC date so a long-running install
/// does not end up with one directory holding hundreds of thousands of renders.
/// </summary>
public sealed class LocalAssetStore(
    IPolloClient polloClient,
    IOptionsMonitor<StorageOptions> options,
    ILogger<LocalAssetStore> logger) : IAssetStore
{
    /// <summary>Pollo's documented retention window for generated media.</summary>
    private static readonly TimeSpan RemoteRetention = TimeSpan.FromDays(14);

    public async Task<MediaAsset> SaveUploadAsync(
        Stream content,
        string fileName,
        string contentType,
        AssetKind kind,
        CancellationToken cancellationToken = default)
    {
        var extension = SafeExtension(Path.GetExtension(fileName), contentType);
        var asset = NewAsset(kind, extension, contentType);

        var absolutePath = EnsurePath(asset.RelativePath);

        await using (var file = File.Create(absolutePath))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        Stamp(asset, absolutePath);

        logger.LogInformation("Stored upload {File} ({Bytes} bytes).", asset.RelativePath, asset.Bytes);
        return asset;
    }

    public async Task<MediaAsset> SaveFromUrlAsync(
        string url,
        AssetKind kind,
        Guid? generationRequestId,
        CancellationToken cancellationToken = default)
    {
        using var response = await polloClient.DownloadAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not download generated asset ({(int)response.StatusCode}) from {url}.");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType
                          ?? DefaultContentType(kind);

        var extension = SafeExtension(Path.GetExtension(new Uri(url).AbsolutePath), contentType);

        var asset = NewAsset(kind, extension, contentType);
        asset.GenerationRequestId = generationRequestId;
        asset.SourceUrl = url;
        asset.RemoteExpiresUtc = DateTimeOffset.UtcNow.Add(RemoteRetention);

        var absolutePath = EnsurePath(asset.RelativePath);

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var file = File.Create(absolutePath))
        {
            await source.CopyToAsync(file, cancellationToken);
        }

        Stamp(asset, absolutePath);

        logger.LogInformation("Downloaded {Kind} asset {File} ({Bytes} bytes) from Pollo.",
            kind, asset.RelativePath, asset.Bytes);

        return asset;
    }

    public string? ResolvePath(MediaAsset asset)
    {
        var absolutePath = Path.Combine(RootPath, asset.RelativePath);
        return File.Exists(absolutePath) ? absolutePath : null;
    }

    public string? BuildPublicUrl(MediaAsset asset)
    {
        var storage = options.CurrentValue;
        return storage.HasPublicBaseUrl
            ? $"{storage.PublicBaseUrl!.TrimEnd('/')}/api/assets/{asset.Id}/raw"
            : null;
    }

    private string RootPath => Path.GetFullPath(options.CurrentValue.Root);

    private static MediaAsset NewAsset(AssetKind kind, string extension, string contentType)
    {
        var id = Guid.CreateVersion7();
        var folder = DateTimeOffset.UtcNow.ToString("yyyy/MM/dd");
        var fileName = $"{id:N}{extension}";

        return new MediaAsset
        {
            Id = id,
            Kind = kind,
            FileName = fileName,
            ContentType = contentType,
            RelativePath = $"{folder}/{fileName}"
        };
    }

    private string EnsurePath(string relativePath)
    {
        var absolutePath = Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        return absolutePath;
    }

    private static void Stamp(MediaAsset asset, string absolutePath)
    {
        var info = new FileInfo(absolutePath);
        asset.Bytes = info.Length;

        using var stream = File.OpenRead(absolutePath);
        asset.Sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// Derives an extension we control rather than trusting the uploaded name. The incoming
    /// filename is attacker-influenced, so it never reaches the filesystem — the stored name
    /// is always a generated GUID plus a whitelisted extension.
    /// </summary>
    private static string SafeExtension(string? candidate, string contentType)
    {
        var normalised = candidate?.ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(normalised) && AllowedExtensions.Contains(normalised))
        {
            return normalised;
        }

        return contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "audio/mpeg" => ".mp3",
            "audio/wav" or "audio/x-wav" => ".wav",
            _ => ".bin"
        };
    }

    private static readonly HashSet<string> AllowedExtensions =
    [
        ".png", ".jpg", ".jpeg", ".webp", ".mp4", ".webm", ".mov", ".mp3", ".wav"
    ];

    private static string DefaultContentType(AssetKind kind) => kind switch
    {
        AssetKind.Video => "video/mp4",
        AssetKind.Image or AssetKind.Thumbnail or AssetKind.SourceImage => "image/png",
        AssetKind.Audio => "audio/mpeg",
        _ => "application/octet-stream"
    };
}
