using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Danaid.Core;

public sealed class CaptureBatchBuffer
{
    private readonly CaptureBatchBufferOptions options;
    private readonly ILogger<CaptureBatchBuffer> logger;
    private readonly Channel<BufferedDelivery> channel;
    private readonly Meter meter = new("Danaid.Ingestion.CaptureBatchBuffer", "1.0.0");
    private readonly Histogram<long> batchCount;
    private readonly Histogram<long> batchBytes;
    private readonly Histogram<double> batchLatencyMs;
    private readonly ActivitySource activitySource = new("Danaid.Ingestion.CaptureBatchBuffer");

    public CaptureBatchBuffer(IOptions<CaptureBatchBufferOptions> options, ILogger<CaptureBatchBuffer> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        channel = Channel.CreateBounded<BufferedDelivery>(
            new BoundedChannelOptions(this.options.Capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        batchCount = meter.CreateHistogram<long>("danaid.batch.size.count");
        batchBytes = meter.CreateHistogram<long>("danaid.batch.size.bytes");
        batchLatencyMs = meter.CreateHistogram<double>("danaid.batch.latency.ms");
    }

    public ValueTask EnqueueAsync(BufferedDelivery delivery, CancellationToken cancellationToken)
        => channel.Writer.WriteAsync(delivery, cancellationToken);

    public async IAsyncEnumerable<CaptureBatch> ReadBatchesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var deliveries = new List<BufferedDelivery>(options.MaxCount);
        long bytes = 0;
        var stopwatch = Stopwatch.StartNew();

        while (await channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (channel.Reader.TryRead(out var delivery))
            {
                if (deliveries.Count == 0)
                {
                    logger.LogDebug("Batch opened.");
                    stopwatch.Restart();
                }

                deliveries.Add(delivery);
                bytes += delivery.Message.Body.Length;

                var flushByCount = deliveries.Count >= options.MaxCount;
                var flushByBytes = bytes >= options.MaxBytes;
                var flushByTime = stopwatch.Elapsed >= options.MaxWait;

                if (flushByCount || flushByBytes || flushByTime)
                {
                    var batch = Flush(deliveries, bytes, stopwatch.Elapsed);
                    yield return batch;

                    deliveries = new List<BufferedDelivery>(options.MaxCount);
                    bytes = 0;
                }
            }

            if (deliveries.Count > 0 && stopwatch.Elapsed >= options.MaxWait)
            {
                var batch = Flush(deliveries, bytes, stopwatch.Elapsed);
                yield return batch;

                deliveries = new List<BufferedDelivery>(options.MaxCount);
                bytes = 0;
            }
        }
    }

    private CaptureBatch Flush(List<BufferedDelivery> deliveries, long bytes, TimeSpan elapsed)
    {
        var batch = new CaptureBatch(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, bytes, deliveries);

        using var activity = activitySource.StartActivity("capture.batch", ActivityKind.Internal);
        activity?.SetTag("batch.id", batch.BatchId);
        activity?.SetTag("batch.count", batch.Deliveries.Count);
        activity?.SetTag("batch.bytes", batch.TotalBytes);
        activity?.SetTag("batch.duration_ms", elapsed.TotalMilliseconds);

        batchCount.Record(batch.Deliveries.Count);
        batchBytes.Record(batch.TotalBytes);
        batchLatencyMs.Record(elapsed.TotalMilliseconds);

        logger.LogInformation("Batch flushed. BatchId={BatchId} Count={Count} Bytes={Bytes}", batch.BatchId, batch.Deliveries.Count, batch.TotalBytes);

        return batch;
    }
}

public sealed class CaptureBatchBufferOptions
{
    public int Capacity { get; init; } = 10_000;
    public int MaxCount { get; init; } = 500;
    public long MaxBytes { get; init; } = 4 * 1024 * 1024;
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(2);
}

public sealed record BufferedDelivery(ulong DeliveryTag, CapturedMessage Message);

public sealed record CapturedMessage(
    byte[] Body,
    IDictionary<string, object?>? Headers,
    string RoutingKey,
    string Exchange,
    string? CorrelationId,
    string? MessageId,
    DateTimeOffset TimestampUtc
);

public sealed record CaptureBatch(
    string BatchId,
    DateTimeOffset CreatedAtUtc,
    long TotalBytes,
    IReadOnlyList<BufferedDelivery> Deliveries
);
