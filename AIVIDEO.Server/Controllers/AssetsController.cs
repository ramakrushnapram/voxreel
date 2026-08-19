using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Contracts;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Data.Entities;
using AIVIDEO.Server.Infrastructure;
using AIVIDEO.Server.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIVIDEO.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/assets")]
public sealed class AssetsController(
    AppDbContext db,
    IAssetStore assetStore,
    IOptionsMonitor<StorageOptions> storageOptions) : ControllerBase
{
    /// <summary>
    /// Streams the stored file.
    ///
    /// Deliberately anonymous. Two callers cannot present a bearer token: Pollo, which fetches
    /// an uploaded source image from its own servers, and the browser's &lt;video&gt;/&lt;img&gt;
    /// tags, which don't send Authorization headers. Access is therefore gated by possession of
    /// the asset's GUIDv7 id — a capability URL — rather than by the owning session. Ids are
    /// unguessable, but anyone holding one can fetch the file, so nothing sensitive should be
    /// served here beyond the user's own generated media. Range requests are enabled for
    /// in-browser video scrubbing.
    /// </summary>
    [HttpGet("{id:guid}/raw")]
    [AllowAnonymous]
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
        asset.UserId = User.GetUserId();

        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(AssetResponse.From(asset));
    }
}
