using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Danaid.Core.Capture;
using Danaid.Core.Storage;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Danaid.Core.Testing.Integration.Observability;

[TestFixture]
[Category("Observability")]
public class CaptureObservabilityIntegrationTests
{
    [Test]
    public async Task WriteAsync_EmitsStructuredLogs_ForSuccessAndFailure()
    {
        var successLogger = new TestLogger<FileSystemStorageWriter>();
        var successWriter = CreateWriter(successLogger);

        var successBatch = CreateBatch("batch-log-success", "msg-1");
        await successWriter.WriteAsync(successBatch, CancellationToken.None);

        var successEntry = successLogger.Entries.Single(x => x.Message.Contains("Batch persisted."));

        var failureLogger = new TestLogger<FileSystemStorageWriter>();
        var failureWriter = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = "bad|path", MaxRetries = 0 }),
            failureLogger);

        var failureBatch = CreateBatch("batch-log-fail", "msg-2");
        var failureResult = await failureWriter.WriteAsync(failureBatch, CancellationToken.None);
        var errorEntry = failureLogger.Entries.Single(x => x.Level == Microsoft.Extensions.Logging.LogLevel.Error);

        Assert.Multiple(() =>
        {
            Assert.That(successEntry.Message, Does.Contain("batch-log-success"));
            Assert.That(failureResult.Success, Is.False);
            Assert.That(failureResult.Error, Is.EqualTo("storage.write_failed"));
            Assert.That(errorEntry.Message, Does.Contain("batch-log-fail"));
            Assert.That(errorEntry.Exception, Is.Null);
            Assert.That(errorEntry.Message, Does.Contain("ErrorCode=storage.write_failed"));
        });
    }

    [Test]
    public async Task CaptureAndStorage_EmitExpectedMetrics()
    {
        var observed = new ConcurrentDictionary<string, long>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Name.StartsWith("danaid.", StringComparison.Ordinal))
                meterListener.EnableMeasurementEvents(instrument);
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            observed.AddOrUpdate(instrument.Name, measurement, (_, existing) => existing + measurement);
        });

        listener.SetMeasurementEventCallback<double>((instrument, measurement, _, _) =>
        {
            observed.AddOrUpdate(instrument.Name, (long)measurement, (_, existing) => existing + (long)measurement);
        });

        listener.Start();

        var buffer = CreateBuffer();
        await FlushSingleBatchAsync(buffer, "m-1");

        var writer = CreateWriter(new TestLogger<FileSystemStorageWriter>());
        await writer.WriteAsync(CreateBatch("batch-metrics", "m-2"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(observed.ContainsKey("danaid.batch.size.count"), Is.True);
            Assert.That(observed.ContainsKey("danaid.batch.size.bytes"), Is.True);
            Assert.That(observed.ContainsKey("danaid.batch.latency.ms"), Is.True);
            Assert.That(observed.ContainsKey("danaid.messages.persisted"), Is.True);
        });
    }

    [Test]
    public async Task CaptureAndStorage_EmitExpectedTraces()
    {
        var started = new List<Activity>();
        var stopped = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Danaid.Ingestion", StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => started.Add(activity),
            ActivityStopped = activity => stopped.Add(activity)
        };

        ActivitySource.AddActivityListener(listener);

        var buffer = CreateBuffer();
        await FlushSingleBatchAsync(buffer, "trace-1");

        var writer = CreateWriter(new TestLogger<FileSystemStorageWriter>());
        await writer.WriteAsync(CreateBatch("batch-trace", "trace-2"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(started.Any(x => x.OperationName == "capture.batch"), Is.True);
            Assert.That(started.Any(x => x.OperationName == "storage.write.batch"), Is.True);
            Assert.That(stopped.Any(x => x.OperationName == "capture.batch"), Is.True);
            Assert.That(stopped.Any(x => x.OperationName == "storage.write.batch"), Is.True);
        });
    }

    private static CaptureBatchBuffer CreateBuffer()
        => new(
            Options.Create(new CaptureBatchBufferOptions
            {
                Capacity = 10,
                MaxCount = 1,
                MaxBytes = 1024,
                MaxWait = TimeSpan.FromMinutes(1)
            }),
            new TestLogger<CaptureBatchBuffer>());

    private static async Task FlushSingleBatchAsync(CaptureBatchBuffer buffer, string messageId)
    {
        await buffer.EnqueueAsync(CreateDelivery(1, messageId), CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var _ in buffer.ReadBatchesAsync(cts.Token))
            break;
    }

    private static FileSystemStorageWriter CreateWriter(TestLogger<FileSystemStorageWriter> logger)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        return new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = tempPath, MaxRetries = 0 }),
            logger);
    }

    private static CaptureBatch CreateBatch(string batchId, string messageId)
        => new(
            batchId,
            DateTimeOffset.UtcNow,
            1,
            new[]
            {
                CreateDelivery(1, messageId)
            });

    private static BufferedDelivery CreateDelivery(ulong tag, string messageId)
        => new(tag, new CapturedMessage(
            body: [1],
            headers: new Dictionary<string, object?> { ["header"] = "value" },
            routingKey: "rk",
            exchange: "ex",
            correlationId: "corr",
            messageId: messageId,
            timestampUtc: DateTimeOffset.UtcNow));
}
