using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Danaid.Core.Capture;

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
