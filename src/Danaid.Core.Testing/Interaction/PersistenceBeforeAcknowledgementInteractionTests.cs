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
[Category("Contract")]
public class PersistenceBeforeAcknowledgementInteractionTests
{
    [Test]
    public async Task AckAfterPersist_AcknowledgesOnlyAfterSuccessfulPersistence()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        var acked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        channel
            .Setup(x => x.BasicAckAsync(It.IsAny<ulong>(), false, It.IsAny<CancellationToken>()))
            .Callback(() => acked.TrySetResult())
            .Returns(ValueTask.CompletedTask);

        channel
            .Setup(x => x.BasicNackAsync(It.IsAny<ulong>(), false, true, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var storageWriter = new Mock<IStorageWriter>();
        storageWriter
            .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorageWriteResult.SuccessResult("fake://persisted"));

        var consumer = CreateConsumer(storageWriter.Object, channel.Object);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var processingTask = InvokeProcessBatchesAsync(consumer, cts.Token);

        await acked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        try { await processingTask; } catch (OperationCanceledException) { }

        storageWriter.Verify(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()), Times.Once);
        channel.Verify(x => x.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
        channel.Verify(x => x.BasicNackAsync(It.IsAny<ulong>(), false, true, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task PersistFailure_RequeuesAndDoesNotAcknowledge()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        var nacked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        channel
            .Setup(x => x.BasicAckAsync(It.IsAny<ulong>(), false, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        channel
            .Setup(x => x.BasicNackAsync(It.IsAny<ulong>(), false, true, It.IsAny<CancellationToken>()))
            .Callback(() => nacked.TrySetResult())
            .Returns(ValueTask.CompletedTask);

        var storageWriter = new Mock<IStorageWriter>();
        storageWriter
            .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorageWriteResult.FailureResult("persistence failed"));

        var consumer = CreateConsumer(storageWriter.Object, channel.Object, (2UL, "msg-2"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var processingTask = InvokeProcessBatchesAsync(consumer, cts.Token);

        await nacked.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        try { await processingTask; } catch (OperationCanceledException) { }

        channel.Verify(x => x.BasicAckAsync(It.IsAny<ulong>(), false, It.IsAny<CancellationToken>()), Times.Never);
        channel.Verify(x => x.BasicNackAsync(2, false, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task MixedPersistenceOutcomes_AckSuccessAndRequeueFailure()
    {
        var channel = new Mock<IChannel>(MockBehavior.Strict);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        channel
            .Setup(x => x.BasicAckAsync(It.IsAny<ulong>(), false, It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        channel
            .Setup(x => x.BasicNackAsync(It.IsAny<ulong>(), false, true, It.IsAny<CancellationToken>()))
            .Callback(() => completion.TrySetResult())
            .Returns(ValueTask.CompletedTask);

        var storageWriter = new Mock<IStorageWriter>();
        storageWriter
            .SetupSequence(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorageWriteResult.SuccessResult("fake://persisted/1"))
            .ReturnsAsync(StorageWriteResult.FailureResult("persist failed"));

        var consumer = CreateConsumer(storageWriter.Object, channel.Object, (10UL, "msg-10"), (11UL, "msg-11"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var processingTask = InvokeProcessBatchesAsync(consumer, cts.Token);

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        try { await processingTask; } catch (OperationCanceledException) { }

        storageWriter.Verify(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        channel.Verify(x => x.BasicAckAsync(10, false, It.IsAny<CancellationToken>()), Times.Once);
        channel.Verify(x => x.BasicNackAsync(11, false, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static RabbitMqConsumer CreateConsumer(IStorageWriter storageWriter, IChannel channel, params (ulong Tag, string MessageId)[] deliveries)
    {
        var batchBuffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions { Capacity = 10, MaxCount = 1, MaxWait = TimeSpan.FromMinutes(1) }),
            new TestLogger<CaptureBatchBuffer>());

        var queuedDeliveries = deliveries.Length == 0 ? new[] { (1UL, "msg-1") } : deliveries;
        foreach (var (tag, messageId) in queuedDeliveries)
            batchBuffer.EnqueueAsync(CreateDelivery(tag, messageId), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        var telemetry = new Mock<IRabbitMqConsumerTelemetry>();
        telemetry.Setup(x => x.SetQueueLag(It.IsAny<int>()));
        telemetry.Setup(x => x.MessageReceived());
        telemetry.Setup(x => x.BatchFailed(It.IsAny<int>()));
        telemetry.Setup(x => x.BatchRetried(It.IsAny<int>()));

        var consumer = new RabbitMqConsumer(
            Options.Create(new RabbitMqConsumerOptions(null) { QueueName = "q", HostName = "h" }),
            batchBuffer,
            storageWriter,
            telemetry.Object,
            new TestLogger<RabbitMqConsumer>());

        SetPrivateField(consumer, "channel", channel);
        return consumer;
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

    private static BufferedDelivery CreateDelivery(ulong deliveryTag, string messageId)
        => new(deliveryTag, new CapturedMessage(
            [1],
            new Dictionary<string, object?> { ["header"] = "value" },
            "rk",
            "ex",
            "corr",
            messageId,
            DateTimeOffset.UtcNow));
}
