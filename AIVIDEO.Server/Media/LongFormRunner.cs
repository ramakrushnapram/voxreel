using System.Threading.Channels;

namespace AIVIDEO.Server.Media;

/// <summary>
/// A process-wide queue of video-project ids waiting to be built. Creating a project enqueues
/// its id and returns immediately; the hosted <see cref="LongFormRunner"/> drains the queue.
/// </summary>
public sealed class LongFormQueue
{
    // Unbounded is fine: enqueue rate is human-driven (one per "make a video" click).
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid projectId) => _channel.Writer.WriteAsync(projectId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Drains the queue and runs each build to completion, one at a time. Serial by design: a
/// single machine assembling videos with FFmpeg and a local LLM shouldn't run several at once
/// and thrash CPU. A failed build is logged and the runner moves on — it must never die.
/// </summary>
public sealed class LongFormRunner(
    LongFormQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<LongFormRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Long-form runner started.");

        await foreach (var projectId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<LongFormService>();
                await service.RunAsync(projectId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // RunAsync already records failure on the project; this is the last-resort guard
                // so one bad build can't take down the runner for everyone.
                logger.LogError(ex, "Unhandled error building project {Id}.", projectId);
            }
        }

        logger.LogInformation("Long-form runner stopped.");
    }
}
