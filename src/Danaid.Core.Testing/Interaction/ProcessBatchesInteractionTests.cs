using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Danaid.Core.Capture;
using Danaid.Core.Consumption;
using Danaid.Core.Storage;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using RabbitMQ.Client;

namespace Danaid.Core.Testing.Interaction
{
    [TestFixture]
    [Category("Interaction")]
    public class ProcessBatchesInteractionTests
    {
        [Test]
        [Category("Contract")]
        public async Task ProcessBatchesAsync_Success_InvokesAck()
        {
            var options = new RabbitMqConsumerOptions(null)
            {
                HostName = "localhost",
                QueueName = "q-test"
            };

            var bufferOptions = new CaptureBatchBufferOptions { MaxCount = 1, MaxWait = TimeSpan.FromSeconds(1), Capacity = 10 };
            var batchBuffer = new CaptureBatchBuffer(Options.Create(bufferOptions), new TestLogger<CaptureBatchBuffer>());

            var storageWriter = new Mock<IStorageWriter>();
            storageWriter
                .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(StorageWriteResult.SuccessResult("fake://location"));

            var telemetry = new Mock<IRabbitMqConsumerTelemetry>();

            var consumer = new RabbitMqConsumer(Options.Create(options), batchBuffer, storageWriter.Object, telemetry.Object, new TestLogger<RabbitMqConsumer>());

            var channel = new Mock<IChannel>();
            var ackTcs = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
            channel.Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask)
                .Callback((ulong tag, bool multiple, CancellationToken ct) => ackTcs.TrySetResult(tag));

            var channelField = typeof(RabbitMqConsumer).GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(channelField, Is.Not.Null);
            channelField.SetValue(consumer, channel.Object);

            var processMethod = typeof(RabbitMqConsumer).GetMethod("ProcessBatchesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(processMethod, Is.Not.Null);

            using var cts = new CancellationTokenSource();

            var processTask = (Task)processMethod.Invoke(consumer, new object[] { cts.Token })!;

            // enqueue a single delivery which will flush immediately because MaxCount = 1
            var delivery = new BufferedDelivery(1, new CapturedMessage(new byte[] { 1 }, new TechnicalMetadata(null!, "rk", "ex", null, null, DateTimeOffset.UtcNow)));
            await batchBuffer.EnqueueAsync(delivery, CancellationToken.None).ConfigureAwait(false);

            // wait for the consumer to call BasicAckAsync (or timeout)
            var completed = await Task.WhenAny(ackTcs.Task, Task.Delay(2000)).ConfigureAwait(false);
            Assert.That(ackTcs.Task.IsCompleted, Is.True, "BasicAckAsync was not called within timeout");
            Assert.That(await ackTcs.Task.ConfigureAwait(false), Is.EqualTo(1ul));

            // cancel and wait for graceful shutdown
            cts.Cancel();
            try
            {
                await processTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // expected when cancelling the background processor
            }
        }
    }
}
