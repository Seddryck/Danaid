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
                    logger.LogWarning(
                        "Batch persistence retry. Attempt={Attempt} ExceptionType={ExceptionType}",
                        retryCount,
                        exception.GetType().Name);
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
            logger.LogError(
                "Batch persistence failed. BatchId={BatchId} ErrorCode={ErrorCode} ExceptionType={ExceptionType}",
                batch.BatchId,
                options.FailureErrorCode,
                ex.GetType().Name);

            return StorageWriteResult.FailureResult(options.FailureErrorCode);
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
            RawPayloadBase64 = Convert.ToBase64String(d.Message.Body),
            TechnicalMetadata = new
            {
                d.Message.MessageId,
                d.Message.CorrelationId,
                d.Message.RoutingKey,
                d.Message.Exchange,
                CaptureTimestampUtc = d.Message.TimestampUtc,
                Headers = NormalizeHeaders(d.Message.Headers)
            }
        });

        await JsonSerializer.SerializeAsync(stream, payload, cancellationToken: cancellationToken);
        return filePath;
    }

    private IDictionary<string, object?>? NormalizeHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null)
            return null;

        var normalized = new Dictionary<string, object?>(headers.Count, StringComparer.Ordinal);

        foreach (var (key, value) in headers)
        {
            if (options.ExcludedHeaderKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;

            normalized[key] = NormalizeValue(value);
        }

        return normalized;
    }

    private object? NormalizeValue(object? value)
        => value switch
        {
            null => null,
            byte[] bytes => Convert.ToBase64String(bytes),
            ReadOnlyMemory<byte> memory => Convert.ToBase64String(memory.ToArray()),
            IReadOnlyList<object?> list => list.Select(NormalizeValue).ToArray(),
            string s => Truncate(s),
            bool or char or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or DateTime or DateTimeOffset or Guid => value,
            _ => value.ToString()
        };

    private string Truncate(string value)
    {
        if (options.MaxHeaderValueLength <= 0)
            return value;

        return value.Length <= options.MaxHeaderValueLength
            ? value
            : value[..options.MaxHeaderValueLength];
    }
}
