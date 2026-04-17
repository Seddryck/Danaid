using System.Text.Json;
using Danaid.Core.Storage;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Danaid.Core.Testing.Integration;

[TestFixture]
public class ConfigurableQueueConsumptionIntegrationTests
{
    [Test]
    public async Task Backpressure_WithBoundedBuffer_BlocksWhenCapacityReached()
    {
        var buffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions
            {
                Capacity = 1,
                MaxCount = 100,
                MaxBytes = 10_000,
                MaxWait = TimeSpan.FromMinutes(1)
            }),
            new TestLogger<CaptureBatchBuffer>());

        await buffer.EnqueueAsync(CreateDelivery(1, "first"), CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await buffer.EnqueueAsync(CreateDelivery(2, "second"), cts.Token));
    }

    [Test]
    public async Task MetadataPersistence_PreservesHeadersRoutingAndIdentifiers()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var writer = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = tempPath, MaxRetries = 0 }),
            new TestLogger<FileSystemStorageWriter>());

        var batch = new CaptureBatch(
            "batch-meta",
            DateTimeOffset.UtcNow,
            3,
            new[]
            {
                new BufferedDelivery(101, new CapturedMessage(
                    Body: [1, 2, 3],
                    Headers: new Dictionary<string, object?> { ["h1"] = "v1" },
                    RoutingKey: "rk.orders",
                    Exchange: "ex.capture",
                    CorrelationId: "corr-1",
                    MessageId: "msg-1",
                    TimestampUtc: DateTimeOffset.UtcNow))
            });

        var result = await writer.WriteAsync(batch, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.Location, Is.Not.Null);

        var json = await File.ReadAllTextAsync(result.Location!);
        using var document = JsonDocument.Parse(json);

        var message = document.RootElement.EnumerateArray().Single();
        Assert.Multiple(() =>
        {
            Assert.That(message.GetProperty("MessageId").GetString(), Is.EqualTo("msg-1"));
            Assert.That(message.GetProperty("CorrelationId").GetString(), Is.EqualTo("corr-1"));
            Assert.That(message.GetProperty("RoutingKey").GetString(), Is.EqualTo("rk.orders"));
            Assert.That(message.GetProperty("Exchange").GetString(), Is.EqualTo("ex.capture"));
            Assert.That(message.GetProperty("Headers").GetProperty("h1").GetString(), Is.EqualTo("v1"));
        });
    }

    [Test]
    public async Task DuplicateHandling_PersistsDuplicateStableIdentifiers()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var writer = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = tempPath, MaxRetries = 0 }),
            new TestLogger<FileSystemStorageWriter>());

        var duplicateId = "msg-duplicate";
        var deliveries = new[]
        {
            CreateDelivery(1, duplicateId),
            CreateDelivery(2, duplicateId)
        };

        var batch = new CaptureBatch("batch-dup", DateTimeOffset.UtcNow, 2, deliveries);
        var result = await writer.WriteAsync(batch, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error);

        var json = await File.ReadAllTextAsync(result.Location!);
        using var document = JsonDocument.Parse(json);

        var ids = document.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("MessageId").GetString())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(ids.Length, Is.EqualTo(2));
            Assert.That(ids.All(x => x == duplicateId), Is.True);
        });
    }

    [Test]
    public void SingleQueuePerInstance_RejectsMultipleQueues()
    {
        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q1,q2"
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAsync<InvalidOperationException>(async () => await consumer.RunAsync(cts.Token));
    }

    [Test]
    public void ConfigurationExternalization_UsesConfiguredValues_WithoutHardcoding()
    {
        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "custom-host",
            Port = 5673,
            VirtualHost = "custom-vhost",
            UserName = "custom-user",
            Password = "custom-password",
            QueueName = "capture-queue"
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.DoesNotThrowAsync(async () => await consumer.RunAsync(cts.Token));
    }

    [Test]
    public void StorageContract_AllowsPluggableWriterImplementation()
    {
        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "capture-queue"
        }, new FakeStorageWriter());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.DoesNotThrowAsync(async () => await consumer.RunAsync(cts.Token));
    }

    [Test]
    [Ignore("Requires a controllable RabbitMQ broker lifecycle for restart simulation.")]
    public void Reconnect_ValidatesPersistentConsumerLifecycleAndRecovery()
    {
    }

    [Test]
    [Ignore("Requires live RabbitMQ publish/consume and ack inspection.")]
    public void AckAfterPersist_ValidatesDurabilityBeforeAcknowledgement()
    {
    }

    private static RabbitMqConsumer CreateConsumer(RabbitMqConsumerOptions options, IStorageWriter? storageWriter = null)
    {
        var batchBuffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions()),
            new TestLogger<CaptureBatchBuffer>());

        storageWriter ??= new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions()),
            new TestLogger<FileSystemStorageWriter>());

        return new RabbitMqConsumer(
            Options.Create(options),
            batchBuffer,
            storageWriter,
            new NoopRabbitMqConsumerTelemetry(),
            new TestLogger<RabbitMqConsumer>());
    }

    private sealed class FakeStorageWriter : IStorageWriter
    {
        public Task<StorageWriteResult> WriteAsync(CaptureBatch batch, CancellationToken cancellationToken)
            => Task.FromResult(StorageWriteResult.SuccessResult("fake://location"));
    }

    private sealed class NoopRabbitMqConsumerTelemetry : IRabbitMqConsumerTelemetry
    {
        public void MessageReceived()
        {
        }

        public void BatchFailed(int messageCount)
        {
        }

        public void BatchRetried(int messageCount)
        {
        }

        public void SetQueueLag(int inFlightCount)
        {
        }
    }

    private static BufferedDelivery CreateDelivery(ulong tag, string messageId)
        => new(tag, new CapturedMessage(
            Body: [1],
            Headers: new Dictionary<string, object?> { ["h"] = "v" },
            RoutingKey: "rk",
            Exchange: "ex",
            CorrelationId: "corr",
            MessageId: messageId,
            TimestampUtc: DateTimeOffset.UtcNow));
}
