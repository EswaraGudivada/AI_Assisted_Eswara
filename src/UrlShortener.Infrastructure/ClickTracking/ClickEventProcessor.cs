using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Domain;

namespace UrlShortener.Infrastructure.ClickTracking;

/// <summary>
/// Single consumer that persists buffered click events in batches and drains the buffer on shutdown.
/// </summary>
internal sealed partial class ClickEventProcessor(
    ChannelClickTracker tracker,
    IServiceScopeFactory scopeFactory,
    IOptions<ClickTrackingOptions> options,
    ILogger<ClickEventProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var reader = tracker.Reader;
        var batchSize = options.Value.BatchSize;
        var batch = new List<ClickEvent>(batchSize);

        try
        {
            while (await reader.WaitToReadAsync(stoppingToken))
            {
                while (batch.Count < batchSize && reader.TryRead(out var clickEvent))
                {
                    batch.Add(clickEvent);
                }

                await PersistAsync(batch);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down; StopAsync drains what is still buffered.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // ExecuteAsync may never have run (stopped before it was scheduled) or may still be running if the
        // host shutdown timeout elapsed; only drain once it is finished so the channel keeps a single reader.
        if (ExecuteTask is null || ExecuteTask.IsCompleted)
        {
            await DrainAsync();
        }
    }

    private async Task DrainAsync()
    {
        var batchSize = options.Value.BatchSize;
        var batch = new List<ClickEvent>(batchSize);
        while (tracker.Reader.TryRead(out var clickEvent))
        {
            batch.Add(clickEvent);
            if (batch.Count >= batchSize)
            {
                await PersistAsync(batch);
            }
        }

        await PersistAsync(batch);
    }

    // Persistence intentionally ignores the stopping token so an in-flight batch is not lost mid-write.
    private async Task PersistAsync(List<ClickEvent> batch)
    {
        if (batch.Count == 0)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IClickEventRepository>();
            await repository.RecordBatchAsync(batch, CancellationToken.None);
        }
        catch (Exception ex)
        {
            LogPersistFailed(ex, batch.Count);
        }
        finally
        {
            batch.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to persist a batch of {Count} click events")]
    private partial void LogPersistFailed(Exception exception, int count);
}
