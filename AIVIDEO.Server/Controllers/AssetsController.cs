using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Contracts;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Data.Entities;
using AIVIDEO.Server.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIVIDEO.Server.Controllers;

[ApiController]
[Route("api/assets")]
public sealed class AssetsController(
    AppDbContext db,
    IAssetStore assetStore,
    IOptionsMonitor<StorageOptions> storageOptions) : ControllerBase
{
    /// <summary>
    /// Streams the stored file.
    ///
    /// This endpoint is also what Pollo calls when Storage:PublicBaseUrl is configured and an
    /// uploaded image is used as an image-to-video source, so it must stay anonymous and must
    /// support range requests for video scrubbing in the browser.
    /// </summary>
    [HttpGet("{id:guid}/raw")]
    public async Task<IActionResult> GetRaw(Guid id, CancellationToken cancellationToken)
    {
        var asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

        if (asset is null)
        {
            return NotFound();
        }

        var path = assetStore.ResolvePath(asset);

        if (path is null)
        {
            // The row exists but the file does not — a deleted or moved media root.
            return NotFound(new ProblemDetails
            {
                Title = "Asset file missing",
                Detail = $"Asset {id} is recorded in the database but not present under the configured storage root.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return PhysicalFile(path, asset.ContentType, enableRangeProcessing: true);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(500L * 1024 * 1024)]
    public async Task<ActionResult<AssetResponse>> Upload(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new ProblemDetails { Title = "No file supplied", Status = StatusCodes.Status400BadRequest });
        }

        var maxBytes = storageOptions.CurrentValue.MaxUploadMb * 1024L * 1024L;
        if (file.Length > maxBytes)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "File too large",
                Detail = $"Maximum upload size is {storageOptions.CurrentValue.MaxUploadMb} MB.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var contentType = file.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!contentType.StartsWith("image/"))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Unsupported file type",
                Detail = "Pollo accepts JPG, PNG, and JPEG source images for image-to-video.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        await using var stream = file.OpenReadStream();
        var asset = await assetStore.SaveUploadAsync(
            stream, file.FileName, contentType, AssetKind.SourceImage, cancellationToken);

        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(AssetResponse.From(asset));
    }
}
