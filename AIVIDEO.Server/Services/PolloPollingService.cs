using AIVIDEO.Server.Configuration;
using AIVIDEO.Server.Data;
using AIVIDEO.Server.Data.Entities;
using AIVIDEO.Server.Pollo;
using AIVIDEO.Server.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIVIDEO.Server.Services;

/// <summary>
/// Drives generations to completion.
///
/// Webhooks are the better mechanism and are used when Pollo:WebhookUrl is set, but they
/// require a publicly reachable server — which local development is not. This service is
/// both the local-development driver and the production reconciliation path for callbacks
/// that never arrive, so it runs in every environment.
/// </summary>
public sealed class PolloPollingService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<PolloOptions> options,
    ILogger<PolloPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Pollo polling service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(3, options.CurrentValue.PollIntervalSeconds));

            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A bad tick must not kill the loop — the next one may well succeed.
                logger.LogError(ex, "Polling tick failed; continuing.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("Pollo polling service stopped.");
    }

    private async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        var opts = options.CurrentValue;

        if (!opts.IsConfigured)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var polloClient = scope.ServiceProvider.GetRequiredService<IPolloClient>();
        var assetStore = scope.ServiceProvider.GetRequiredService<IAssetStore>();

        var now = DateTimeOffset.UtcNow;

        var pending = await db.GenerationRequests
            .Where(g => (g.Status == GenerationStatus.Submitted || g.Status == GenerationStatus.Processing)
                        && g.PolloTaskId != null
                        && (g.NextPollUtc == null || g.NextPollUtc <= now))
            .OrderBy(g => g.NextPollUtc)
            .Take(opts.MaxConcurrentTasks)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var generation in pending)
        {
            await ProcessAsync(db, polloClient, assetStore, generation, opts, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ProcessAsync(
        AppDbContext db,
        IPolloClient polloClient,
        IAssetStore assetStore,
        GenerationRequest generation,
        PolloOptions opts,
        CancellationToken cancellationToken)
    {
        // Abandon tasks that have outlived the timeout rather than polling them forever.
        var age = DateTimeOffset.UtcNow - (generation.SubmittedUtc ?? generation.CreatedUtc);
        if (age > TimeSpan.FromMinutes(opts.TaskTimeoutMinutes))
        {
            generation.Status = GenerationStatus.Failed;
            generation.FailMessage = $"Timed out after {opts.TaskTimeoutMinutes} minutes without completing.";
            generation.CompletedUtc = DateTimeOffset.UtcNow;
            logger.LogWarning("Generation {Id} timed out.", generation.Id);
            return;
        }

        PolloTaskStatusResponse status;
        try
        {
            status = await polloClient.GetTaskStatusAsync(generation.PolloTaskId!, cancellationToken);
        }
        catch (PolloApiException ex)
        {
            // A transient status-check failure is not a failed render. Back off and retry;
            // the timeout above is what eventually gives up.
            generation.NextPollUtc = DateTimeOffset.UtcNow.AddSeconds(opts.PollIntervalSeconds * 2);
            logger.LogWarning(ex, "Status check failed for generation {Id}; backing off.", generation.Id);
            return;
        }

        generation.CostUsd = status.CostUsd ?? generation.CostUsd;
        generation.Credit = status.Credit ?? generation.Credit;

        var generations = status.Generations ?? [];

        if (generations.Count == 0)
        {
            generation.Status = GenerationStatus.Processing;
            generation.NextPollUtc = DateTimeOffset.UtcNow.AddSeconds(opts.PollIntervalSeconds);
            return;
        }

        if (generations.Any(g => !PolloStatus.IsTerminal(g.Status)))
        {
            generation.Status = GenerationStatus.Processing;
            generation.NextPollUtc = DateTimeOffset.UtcNow.AddSeconds(opts.PollIntervalSeconds);
            return;
        }

        var succeeded = generations.Where(g => PolloStatus.IsSuccess(g.Status) && !string.IsNullOrWhiteSpace(g.Url)).ToList();

        if (succeeded.Count == 0)
        {
            generation.Status = GenerationStatus.Failed;
            generation.FailMessage = generations.FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.FailMsg))?.FailMsg
                                     ?? "Pollo reported failure without a message.";
            generation.CompletedUtc = DateTimeOffset.UtcNow;
            logger.LogWarning("Generation {Id} failed: {Message}", generation.Id, generation.FailMessage);
            return;
        }

        generation.Status = GenerationStatus.Downloading;

        foreach (var result in succeeded)
        {
            try
            {
                var kind = ParseAssetKind(result.MediaType);
                var asset = await assetStore.SaveFromUrlAsync(result.Url!, kind, generation.Id, cancellationToken);
                db.MediaAssets.Add(asset);

                // The cover frame doubles as a gallery thumbnail and a YouTube thumbnail
                // candidate, and it expires on the same 14-day clock, so grab it now.
                if (!string.IsNullOrWhiteSpace(result.Cover))
                {
                    var cover = await assetStore.SaveFromUrlAsync(
                        result.Cover, AssetKind.Thumbnail, generation.Id, cancellationToken);
                    db.MediaAssets.Add(cover);
                }
            }
            catch (Exception ex)
            {
                // Download failure is terminal for this generation: the URL has a 14-day life
                // and no local copy means nothing usable, so surface it rather than retrying blindly.
                generation.Status = GenerationStatus.Failed;
                generation.FailMessage = $"Generated successfully but the asset could not be downloaded: {ex.Message}";
                generation.CompletedUtc = DateTimeOffset.UtcNow;
                logger.LogError(ex, "Asset download failed for generation {Id}.", generation.Id);
                return;
            }
        }

        generation.Status = GenerationStatus.Succeeded;
        generation.CompletedUtc = DateTimeOffset.UtcNow;
        generation.NextPollUtc = null;

        logger.LogInformation("Generation {Id} succeeded with {Count} asset(s).", generation.Id, succeeded.Count);
    }

    private static AssetKind ParseAssetKind(string? mediaType) => mediaType?.ToLowerInvariant() switch
    {
        "video" => AssetKind.Video,
        "image" => AssetKind.Image,
        "audio" => AssetKind.Audio,
        _ => AssetKind.Video
    };
}
