using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Danaid.Core.Capture;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;

namespace Danaid.Core.Storage;

public sealed class FileSystemStorageWriter : IStorageWriter
{
    private readonly FileSystemStorageWriterOptions options;
    private readonly ILogger<FileSystemStorageWriter> logger;
    private readonly AsyncPolicy retryPolicy;
    private readonly Meter meter = new("Danaid.Ingestion.StorageWriter", "1.0.0");
    private readonly Counter<long> persistedCounter;
    private readonly Counter<long> failedCounter;
    private readonly Counter<long> retryCounter;
    private readonly Histogram<double> persistenceLatencyMs;
    private readonly ActivitySource activitySource = new("Danaid.Ingestion.StorageWriter");

    public FileSystemStorageWriter(IOptions<FileSystemStorageWriterOptions> options, ILogger<FileSystemStorageWriter> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        persistedCounter = meter.CreateCounter<long>("danaid.messages.persisted");
        failedCounter = meter.CreateCounter<long>("danaid.messages.failed");
        retryCounter = meter.CreateCounter<long>("danaid.messages.retry");
        persistenceLatencyMs = meter.CreateHistogram<double>("danaid.storage.persistence.latency.ms");

        retryPolicy = Policy
            .Handle<IOException>()
            .Or<TimeoutException>()
            .WaitAndRetryAsync(
                retryCount: Math.Max(0, this.options.MaxRetries),
                sleepDurationProvider: _ => this.options.RetryDelay,
                onRetry: (exception, _, retryCount, _) =>
                {
                    retryCounter.Add(1);
                    logger.LogWarning(exception, "Batch persistence retry. Attempt={Attempt}", retryCount);
                });
    }

    public async Task<StorageWriteResult> WriteAsync(CaptureBatch batch, CancellationToken cancellationToken)
    {
        using var activity = activitySource.StartActivity("storage.write.batch", ActivityKind.Internal);
        activity?.SetTag("batch.id", batch.BatchId);
        activity?.SetTag("batch.count", batch.Deliveries.Count);
        activity?.SetTag("batch.bytes", batch.TotalBytes);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            string? filePath = null;
            await retryPolicy.ExecuteAsync(async ct =>
            {
                filePath = await PersistBatchAsync(batch, ct);
            }, cancellationToken);

            persistedCounter.Add(batch.Deliveries.Count);
            persistenceLatencyMs.Record(stopwatch.Elapsed.TotalMilliseconds);

            logger.LogInformation("Batch persisted. BatchId={BatchId} FilePath={FilePath} Count={Count}", batch.BatchId, filePath, batch.Deliveries.Count);

            return StorageWriteResult.SuccessResult(filePath!);
        }
        catch (Exception ex)
        {
            failedCounter.Add(batch.Deliveries.Count);
            persistenceLatencyMs.Record(stopwatch.Elapsed.TotalMilliseconds);
            logger.LogError(ex, "Batch persistence failed. BatchId={BatchId}", batch.BatchId);
            return StorageWriteResult.FailureResult(ex.Message);
        }
    }

    private async Task<string> PersistBatchAsync(CaptureBatch batch, CancellationToken cancellationToken)
    {
        var folder = Path.Combine(options.BasePath, DateTime.UtcNow.ToString("yyyyMMdd"));
        Directory.CreateDirectory(folder);

        var filePath = Path.Combine(folder, $"{batch.BatchId}.json");
        await using var stream = File.Create(filePath);

        var payload = batch.Deliveries.Select(d => new
        {
            d.DeliveryTag,
            d.Message.MessageId,
            d.Message.CorrelationId,
            d.Message.RoutingKey,
            d.Message.Exchange,
            d.Message.TimestampUtc,
            Headers = d.Message.Headers,
            BodyBase64 = Convert.ToBase64String(d.Message.Body)
        });

        await JsonSerializer.SerializeAsync(stream, payload, cancellationToken: cancellationToken);
        return filePath;
    }
}
