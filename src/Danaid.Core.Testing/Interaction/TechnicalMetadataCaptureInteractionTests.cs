using System.Reflection;
using Danaid.Core.Capture;
using Danaid.Core.Consumption;
using Danaid.Core.Storage;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using RabbitMQ.Client;

namespace Danaid.Core.Testing.Interaction;

[TestFixture]
[Category("Functional")]
public class TechnicalMetadataCaptureInteractionTests
{
    [Test]
    [Category("Contract")]
    public async Task CaptureFlow_SendsRawPayloadAndTechnicalMetadataTogetherToPersistence()
    {
        var capturedBatch = await RunSingleDeliveryFlowAndCaptureBatchAsync(
            CreateDelivery(1, [1, 2, 3], "msg-1", "corr-1"));

        var persisted = capturedBatch.Deliveries.Single().Message;

        Assert.Multiple(() =>
        {
            Assert.That(persisted.Body, Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(persisted.TechnicalMetadata, Is.Not.Null);
            Assert.That(persisted.TechnicalMetadata.RoutingKey, Is.EqualTo("rk"));
            Assert.That(persisted.TechnicalMetadata.Exchange, Is.EqualTo("ex"));
        });
    }

    [Test]
    [Category("Contract")]
    public async Task PersistenceRequest_IncludesMessageAndCorrelationIdentifiers_WhenAvailable()
    {
        var capturedBatch = await RunSingleDeliveryFlowAndCaptureBatchAsync(
            CreateDelivery(2, [9], "msg-2", "corr-2"));

        var persisted = capturedBatch.Deliveries.Single().Message;

        Assert.Multiple(() =>
        {
            Assert.That(persisted.MessageId, Is.EqualTo("msg-2"));
            Assert.That(persisted.CorrelationId, Is.EqualTo("corr-2"));
        });
    }

    [Test]
    [Category("Contract")]
    public async Task MissingMetadataFields_AreForwardedAsNullWithoutFallbackBusinessMapping()
    {
        var capturedBatch = await RunSingleDeliveryFlowAndCaptureBatchAsync(
            CreateDelivery(3, [7], null, null, headers: new Dictionary<string, object?>()));

        var persisted = capturedBatch.Deliveries.Single().Message;

        Assert.Multiple(() =>
        {
            Assert.That(persisted.MessageId, Is.Null);
            Assert.That(persisted.CorrelationId, Is.Null);
            Assert.That(persisted.Headers, Is.Null.Or.Empty);
        });
    }

    private static async Task<CaptureBatch> RunSingleDeliveryFlowAndCaptureBatchAsync(BufferedDelivery delivery)
    {
        CaptureBatch? capturedBatch = null;

        var storageWriter = new Mock<IStorageWriter>();
        storageWriter
            .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
            .Callback<CaptureBatch, CancellationToken>((batch, _) => capturedBatch = batch)
            .ReturnsAsync(StorageWriteResult.SuccessResult("fake://location"));

        var channel = new Mock<IChannel>(MockBehavior.Strict);
        channel
            .Setup(x => x.BasicAckAsync(It.IsAny<ulong>(), false, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        channel
            .Setup(x => x.BasicNackAsync(It.IsAny<ulong>(), false, true, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var batchBuffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions { Capacity = 10, MaxCount = 1, MaxWait = TimeSpan.FromMinutes(1) }),
            new TestLogger<CaptureBatchBuffer>());

        await batchBuffer.EnqueueAsync(delivery, CancellationToken.None);

        var telemetry = new Mock<IRabbitMqConsumerTelemetry>();
        telemetry.Setup(x => x.SetQueueLag(It.IsAny<int>()));
        telemetry.Setup(x => x.MessageReceived());
        telemetry.Setup(x => x.BatchFailed(It.IsAny<int>()));
        telemetry.Setup(x => x.BatchRetried(It.IsAny<int>()));

        var consumer = new RabbitMqConsumer(
            Options.Create(new RabbitMqConsumerOptions(null) { QueueName = "q", HostName = "h" }),
            batchBuffer,
            storageWriter.Object,
            telemetry.Object,
            new TestLogger<RabbitMqConsumer>());

        SetPrivateField(consumer, "channel", channel.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var processingTask = InvokeProcessBatchesAsync(consumer, cts.Token);

        await Task.Delay(200);
        cts.Cancel();

        try { await processingTask; } catch (OperationCanceledException) { }

        Assert.That(capturedBatch, Is.Not.Null);
        return capturedBatch!;
    }

    private static async Task InvokeProcessBatchesAsync(RabbitMqConsumer consumer, CancellationToken cancellationToken)
    {
        var method = typeof(RabbitMqConsumer).GetMethod("ProcessBatchesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        var task = (Task?)method!.Invoke(consumer, [cancellationToken]);
        Assert.That(task, Is.Not.Null);

        await task!;
    }

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(instance, value);
    }

    private static BufferedDelivery CreateDelivery(ulong deliveryTag, byte[] body, string? messageId, string? correlationId, IDictionary<string, object?>? headers = null)
        => new(deliveryTag, new CapturedMessage(
            body: body,
            headers: headers ?? new Dictionary<string, object?> { ["header"] = "value" },
            routingKey: "rk",
            exchange: "ex",
            correlationId: correlationId,
            messageId: messageId,
            timestampUtc: DateTimeOffset.UtcNow));
}
