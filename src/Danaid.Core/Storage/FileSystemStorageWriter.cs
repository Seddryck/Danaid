using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Danaid.Core.Storage;

public sealed class FileSystemStorageWriter : IStorageWriter
{
    private readonly FileSystemStorageWriterOptions options;
    private readonly ILogger<FileSystemStorageWriter> logger;
    private readonly Meter meter = new("Danaid.Ingestion.StorageWriter", "1.0.0");
    private readonly Counter<long> persistedCounter;
    private readonly Counter<long> failedCounter;
    private readonly Counter<long> retryCounter;
    private readonly ActivitySource activitySource = new("Danaid.Ingestion.StorageWriter");

    public FileSystemStorageWriter(IOptions<FileSystemStorageWriterOptions> options, ILogger<FileSystemStorageWriter> logger)
    {
        this.options = options.Value;
        this.logger = logger;

        persistedCounter = meter.CreateCounter<long>("danaid.messages.persisted");
        failedCounter = meter.CreateCounter<long>("danaid.messages.failed");
        retryCounter = meter.CreateCounter<long>("danaid.messages.retry");
    }

    public async Task<StorageWriteResult> WriteAsync(CaptureBatch batch, CancellationToken cancellationToken)
    {
        using var activity = activitySource.StartActivity("storage.write.batch", ActivityKind.Internal);
        activity?.SetTag("batch.id", batch.BatchId);
        activity?.SetTag("batch.count", batch.Deliveries.Count);
        activity?.SetTag("batch.bytes", batch.TotalBytes);

        var attempt = 0;
        while (attempt <= options.MaxRetries)
        {
            try
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

                persistedCounter.Add(batch.Deliveries.Count);
                logger.LogInformation("Batch persisted. BatchId={BatchId} FilePath={FilePath} Count={Count}", batch.BatchId, filePath, batch.Deliveries.Count);

                return StorageWriteResult.SuccessResult(filePath);
            }
            catch (Exception ex) when (attempt < options.MaxRetries)
            {
                attempt++;
                retryCounter.Add(1);
                logger.LogWarning(ex, "Batch persistence retry. BatchId={BatchId} Attempt={Attempt}", batch.BatchId, attempt);
                await Task.Delay(options.RetryDelay, cancellationToken);
            }
            catch (Exception ex)
            {
                failedCounter.Add(batch.Deliveries.Count);
                logger.LogError(ex, "Batch persistence failed. BatchId={BatchId}", batch.BatchId);
                return StorageWriteResult.FailureResult(ex.Message);
            }
        }

        failedCounter.Add(batch.Deliveries.Count);
        return StorageWriteResult.FailureResult("Storage persistence failed.");
    }
}
