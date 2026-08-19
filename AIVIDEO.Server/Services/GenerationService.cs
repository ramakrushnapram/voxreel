using System.Text.Json;
using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Contracts;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Data.Entities;
using AIVIDEO.Server.Pollo;
using AIVIDEO.Server.Providers;
using AIVIDEO.Server.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIVIDEO.Server.Services;

/// <summary>
/// Raised for caller mistakes (bad length, unreachable image) so controllers can return 400
/// instead of a 500. Distinct from <see cref="PolloApiException"/>, which means Pollo rejected us.
/// </summary>
public sealed class GenerationValidationException(string message) : Exception(message);

/// <summary>
/// Creates generation rows and submits them to Pollo.
///
/// Submission is deliberately synchronous with the HTTP request while completion is not:
/// Pollo returns a task id in well under a second, so the caller gets immediate confirmation
/// and a row to poll, and the minutes-long wait for the render is handled by
/// <see cref="PolloPollingService"/>.
/// </summary>
public sealed class GenerationService(
    AppDbContext db,
    IPolloClient polloClient,
    IAssetStore assetStore,
    FreeImageProvider freeImageProvider,
    IOptionsMonitor<PolloOptions> polloOptions,
    ILogger<GenerationService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<GenerationRequest> CreateTextToVideoAsync(
        Guid userId,
        CreateTextToVideoRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateLength(request.Length);

        var opts = polloOptions.CurrentValue;
        var model = ResolveRole(opts.Models, request.Role);

        var input = new PolloInput
        {
            Prompt = request.Prompt,
            Length = request.Length,
            Resolution = request.Resolution,
            AspectRatio = request.AspectRatio,
            Mode = request.Mode,
            GenerateAudio = request.GenerateAudio
        };

        var entity = new GenerationRequest
        {
            UserId = userId,
            Kind = GenerationKind.TextToVideo,
            Role = request.Role,
            Model = model,
            Prompt = request.Prompt,
            Length = request.Length,
            Resolution = request.Resolution,
            AspectRatio = request.AspectRatio,
            Mode = request.Mode,
            GenerateAudio = request.GenerateAudio
        };

        return await SubmitAsync(entity, input, cancellationToken);
    }

    public async Task<GenerationRequest> CreateImageToVideoAsync(
        Guid userId,
        CreateImageToVideoRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateLength(request.Length);

        var imageUrl = await ResolveImageUrlAsync(userId, request.ImageUrl, request.AssetId, cancellationToken);

        var opts = polloOptions.CurrentValue;
        var model = opts.Models.ImageToVideo;

        var input = new PolloInput
        {
            Image = imageUrl,
            Prompt = string.IsNullOrWhiteSpace(request.Prompt) ? null : request.Prompt,
            Length = request.Length,
            Resolution = request.Resolution,
            Mode = request.Mode,
            GenerateAudio = request.GenerateAudio
        };

        var entity = new GenerationRequest
        {
            UserId = userId,
            Kind = GenerationKind.ImageToVideo,
            Role = nameof(PolloModelOptions.ImageToVideo),
            Model = model,
            Prompt = request.Prompt,
            SourceImageUrl = imageUrl,
            Length = request.Length,
            Resolution = request.Resolution,
            Mode = request.Mode,
            GenerateAudio = request.GenerateAudio
        };

        return await SubmitAsync(entity, input, cancellationToken);
    }

    public async Task<GenerationRequest> CreateImageAsync(
        Guid userId,
        CreateImageRequest request,
        CancellationToken cancellationToken = default)
    {
        var opts = polloOptions.CurrentValue;

        var hasSource = request.SourceAssetId is not null || !string.IsNullOrWhiteSpace(request.SourceImageUrl);

        if (UseFreeProvider(request.Provider, opts.IsConfigured))
        {
            // The free provider is text-to-image only; editing an existing image needs Pollo.
            if (hasSource)
            {
                throw new GenerationValidationException(
                    "Editing an uploaded image needs a Pollo API key. The free provider generates from a prompt only. " +
                    "Set Provider to \"pollo\" with a key configured, or remove the source image.");
            }

            return await CreateFreeImageAsync(userId, request, cancellationToken);
        }

        var model = opts.Models.Still;

        string? sourceUrl = null;
        if (hasSource)
        {
            sourceUrl = await ResolveImageUrlAsync(userId, request.SourceImageUrl, request.SourceAssetId, cancellationToken);
        }

        var input = new PolloInput
        {
            Prompt = request.Prompt,
            AspectRatio = request.AspectRatio,
            Resolution = request.Resolution,
            // Image models take the edit source as "imageUrl"; video models take "image".
            // Sending the wrong one silently produces a text-only generation.
            ImageUrl = sourceUrl
        };

        var entity = new GenerationRequest
        {
            UserId = userId,
            Kind = sourceUrl is null ? GenerationKind.Image : GenerationKind.ImageEdit,
            Role = nameof(PolloModelOptions.Still),
            Model = model,
            Prompt = request.Prompt,
            SourceImageUrl = sourceUrl,
            Resolution = request.Resolution,
            AspectRatio = request.AspectRatio
        };

        return await SubmitAsync(entity, input, cancellationToken);
    }

    // Both reads filter by UserId so one user can never fetch another's generation by id.
    public async Task<GenerationRequest?> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken = default) =>
        await db.GenerationRequests
            .Include(g => g.Assets)
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, cancellationToken);

    /// <summary>
    /// Deletes one of the caller's generations and its asset rows (cascade). Scoped by UserId
    /// so a user can only delete their own; returns false if it isn't theirs or doesn't exist.
    /// The files on disk are left in place — harmless, and cheaper than tracking every path here.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await db.GenerationRequests
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId, cancellationToken);

        if (entity is null)
        {
            return false;
        }

        db.GenerationRequests.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<GenerationRequest>> ListAsync(
        Guid userId,
        int take = 50,
        CancellationToken cancellationToken = default) =>
        await db.GenerationRequests
            .Where(g => g.UserId == userId)
            .Include(g => g.Assets)
            .OrderByDescending(g => g.CreatedUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// "auto" picks free when Pollo is not configured; "free" always uses it; "pollo" never does.
    /// The auto behaviour is what makes image generation work out of the box with no key.
    /// </summary>
    private static bool UseFreeProvider(string provider, bool polloConfigured) => provider.ToLowerInvariant() switch
    {
        "free" => true,
        "pollo" => false,
        _ => !polloConfigured
    };

    /// <summary>
    /// Free path (Pollinations). Synchronous: the image is fetched and saved within the call,
    /// so the row is created already Succeeded rather than left for the poller. On failure the
    /// row is kept as Failed with the reason, matching the Pollo path's behaviour.
    /// </summary>
    private async Task<GenerationRequest> CreateFreeImageAsync(
        Guid userId,
        CreateImageRequest request,
        CancellationToken cancellationToken)
    {
        var entity = new GenerationRequest
        {
            UserId = userId,
            Kind = GenerationKind.Image,
            Role = nameof(PolloModelOptions.Still),
            Model = FreeImageProvider.ModelName,
            Prompt = request.Prompt,
            Resolution = request.Resolution,
            AspectRatio = request.AspectRatio,
            CostUsd = 0m,
            RequestJson = "{\"provider\":\"free\"}"
        };

        // Generate before touching the database, then persist the row exactly once with the
        // asset already attached. A single INSERT (rather than insert-then-update) is both
        // simpler and avoids a concurrency fault when the same row is updated moments later.
        try
        {
            // Vary the seed per row so repeated prompts don't return a CDN-cached image.
            var seed = (int)(entity.Id.GetHashCode() & 0x7fffffff);
            var asset = await freeImageProvider.GenerateAsync(
                request.Prompt, request.AspectRatio, request.Resolution, userId, seed, cancellationToken);

            asset.GenerationRequestId = entity.Id;
            entity.Assets.Add(asset);
            entity.Status = GenerationStatus.Succeeded;
            entity.CompletedUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            entity.Status = GenerationStatus.Failed;
            entity.FailMessage = ex.Message;
            entity.CompletedUtc = DateTimeOffset.UtcNow;
            logger.LogWarning(ex, "Free image generation failed for {Id}.", entity.Id);
        }

        db.GenerationRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private async Task<GenerationRequest> SubmitAsync(
        GenerationRequest entity,
        PolloInput input,
        CancellationToken cancellationToken)
    {
        var opts = polloOptions.CurrentValue;

        var payload = new PolloGenerationRequest
        {
            Input = input,
            // Only send a webhook URL when one is actually reachable. Pointing Pollo at an
            // unreachable localhost callback would leave tasks completing with no notification.
            WebhookUrl = string.IsNullOrWhiteSpace(opts.WebhookUrl) ? null : opts.WebhookUrl,
            ClientSource = opts.ClientSource
        };

        entity.RequestJson = JsonSerializer.Serialize(payload, JsonOptions);

        db.GenerationRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var created = await polloClient.CreateGenerationAsync(entity.Model, payload, cancellationToken);

            entity.PolloTaskId = created.TaskId;
            entity.Status = GenerationStatus.Submitted;
            entity.SubmittedUtc = DateTimeOffset.UtcNow;
            entity.NextPollUtc = DateTimeOffset.UtcNow.AddSeconds(opts.PollIntervalSeconds);
            entity.Attempts++;
        }
        catch (PolloApiException ex)
        {
            // The row is kept rather than rolled back: a failed submission with its exact
            // request JSON is the most useful thing to have when diagnosing a bad payload.
            entity.Status = GenerationStatus.Failed;
            entity.FailMessage = ex.Message;
            entity.CompletedUtc = DateTimeOffset.UtcNow;
            logger.LogError(ex, "Submission failed for generation {Id}.", entity.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>
    /// Turns either a caller-supplied URL or an uploaded asset into a URL Pollo can fetch.
    ///
    /// The asset path is the one that bites people: Pollo downloads the image from their own
    /// servers, so a local file is only usable when Storage:PublicBaseUrl exposes this server
    /// to the internet. Failing here with an explanation beats failing at Pollo with a fetch error.
    /// </summary>
    private async Task<string> ResolveImageUrlAsync(
        Guid userId,
        string? imageUrl,
        Guid? assetId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(imageUrl) && assetId is not null)
        {
            throw new GenerationValidationException("Supply either imageUrl or assetId, not both.");
        }

        if (!string.IsNullOrWhiteSpace(imageUrl))
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new GenerationValidationException("imageUrl must be an absolute http(s) URL.");
            }

            return imageUrl;
        }

        if (assetId is null)
        {
            throw new GenerationValidationException("Supply either imageUrl or assetId.");
        }

        // Scoped by UserId: referencing another user's uploaded asset must look identical to
        // the asset not existing, so ownership cannot be probed by guessing ids.
        var asset = await db.MediaAssets.FirstOrDefaultAsync(a => a.Id == assetId && a.UserId == userId, cancellationToken)
                    ?? throw new GenerationValidationException($"Asset {assetId} was not found.");

        var publicUrl = assetStore.BuildPublicUrl(asset);

        if (publicUrl is null)
        {
            throw new GenerationValidationException(
                "This asset cannot be sent to Pollo because Storage:PublicBaseUrl is not configured. " +
                "Pollo fetches source images over the internet and cannot reach localhost. " +
                "Either expose this server with a tunnel and set Storage:PublicBaseUrl, " +
                "or pass an already-public imageUrl instead.");
        }

        return publicUrl;
    }

    private static void ValidateLength(int length)
    {
        if (!PolloLimits.IsAllowedLength(length))
        {
            throw new GenerationValidationException(
                $"length must be one of {string.Join(", ", PolloLimits.AllowedLengths)}. " +
                $"Pollo caps a single clip at {PolloLimits.MaxClipSeconds}s — longer runtimes are " +
                "produced by assembling many clips, not by raising this value.");
        }
    }

    private static string ResolveRole(PolloModelOptions models, string role) => role.ToLowerInvariant() switch
    {
        "hero" => models.Hero,
        "broll" or "b-roll" => models.Broll,
        "imagetovideo" => models.ImageToVideo,
        "characterlock" => models.CharacterLock,
        "still" => models.Still,
        _ => throw new GenerationValidationException(
            $"Unknown role '{role}'. Expected Hero, Broll, ImageToVideo, CharacterLock, or Still.")
    };
}
