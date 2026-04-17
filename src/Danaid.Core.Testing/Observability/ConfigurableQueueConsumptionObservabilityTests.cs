using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Danaid.Core.Storage;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Danaid.Core.Testing.Observability;

[TestFixture]
public class ConfigurableQueueConsumptionObservabilityTests
{
    [Test]
    public async Task VerifyRequiredLogs_EmitsBatchAndErrorContext()
    {
        var successLogger = new TestLogger<FileSystemStorageWriter>();
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var successWriter = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = tempPath, MaxRetries = 0 }),
            successLogger);

        var successBatch = CreateBatch("batch-log-success", "msg-1");
        await successWriter.WriteAsync(successBatch, CancellationToken.None);

        var successEntry = successLogger.Entries.Single(x => x.Message.Contains("Batch persisted."));
        Assert.That(successEntry.Message, Does.Contain("batch-log-success"));

        var failureLogger = new TestLogger<FileSystemStorageWriter>();
        var failureWriter = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = "bad|path", MaxRetries = 0 }),
            failureLogger);

        var failureBatch = CreateBatch("batch-log-fail", "msg-2");
        var failureResult = await failureWriter.WriteAsync(failureBatch, CancellationToken.None);

        Assert.That(failureResult.Success, Is.False);

        var errorEntry = failureLogger.Entries.Single(x => x.Level == Microsoft.Extensions.Logging.LogLevel.Error);
        Assert.Multiple(() =>
        {
            Assert.That(errorEntry.Message, Does.Contain("batch-log-fail"));
            Assert.That(errorEntry.Exception, Is.Not.Null);
        });
    }

    [Test]
    public async Task MetricsAreEmitted_ForRequiredStorageAndBatchInstruments()
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

        var buffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions
            {
                Capacity = 10,
                MaxCount = 1,
                MaxBytes = 1024,
                MaxWait = TimeSpan.FromMinutes(1)
            }),
            new TestLogger<CaptureBatchBuffer>());

        await buffer.EnqueueAsync(CreateDelivery(1, "m-1"), CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var _ in buffer.ReadBatchesAsync(cts.Token))
            break;

        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var writer = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = tempPath, MaxRetries = 0 }),
            new TestLogger<FileSystemStorageWriter>());

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
    public async Task TraceSpansAreEmitted_ForBatchAndStorageOperations()
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

        var buffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions
            {
                Capacity = 10,
                MaxCount = 1,
                MaxBytes = 1024,
                MaxWait = TimeSpan.FromMinutes(1)
            }),
            new TestLogger<CaptureBatchBuffer>());

        await buffer.EnqueueAsync(CreateDelivery(1, "trace-1"), CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await foreach (var _ in buffer.ReadBatchesAsync(cts.Token))
            break;

        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var writer = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = tempPath, MaxRetries = 0 }),
            new TestLogger<FileSystemStorageWriter>());

        await writer.WriteAsync(CreateBatch("batch-trace", "trace-2"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(started.Any(x => x.OperationName == "capture.batch"), Is.True);
            Assert.That(started.Any(x => x.OperationName == "storage.write.batch"), Is.True);
            Assert.That(stopped.Any(x => x.OperationName == "capture.batch"), Is.True);
            Assert.That(stopped.Any(x => x.OperationName == "storage.write.batch"), Is.True);
        });
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
            Body: [1],
            Headers: new Dictionary<string, object?> { ["header"] = "value" },
            RoutingKey: "rk",
            Exchange: "ex",
            CorrelationId: "corr",
            MessageId: messageId,
            TimestampUtc: DateTimeOffset.UtcNow));
}
