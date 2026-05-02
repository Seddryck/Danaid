using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    public class RabbitMqConsumerInteractionTests
    {
        [Test]
        [Category("Contract")]
        public async Task AckBatchAsync_InvokesBasicAckForEachDelivery()
        {
            var consumer = CreateMinimalConsumer();

            // prepare a mock channel and inject it
            var channel = new Mock<IChannel>();
            channel
                .Setup(c => c.BasicAckAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            // put two in-flight deliveries so RemoveInFlight updates queue lag
            var inFlightField = typeof(RabbitMqConsumer).GetField("inFlight", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(inFlightField, Is.Not.Null);
            var inFlight = (ConcurrentDictionary<ulong, BufferedDelivery>)inFlightField.GetValue(consumer)!;
            inFlight[1] = new BufferedDelivery(1, new CapturedMessage(new byte[] { 1 }, new TechnicalMetadata(null!, "", "", null, null, DateTimeOffset.UtcNow)));
            inFlight[2] = new BufferedDelivery(2, new CapturedMessage(new byte[] { 2 }, new TechnicalMetadata(null!, "", "", null, null, DateTimeOffset.UtcNow)));

            var channelField = typeof(RabbitMqConsumer).GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(channelField, Is.Not.Null);
            channelField.SetValue(consumer, channel.Object);

            var batch = new CaptureBatch(
                BatchId: Guid.NewGuid().ToString(),
                CreatedAtUtc: DateTimeOffset.UtcNow,
                TotalBytes: 2,
                Deliveries: new List<BufferedDelivery>
                {
                    new BufferedDelivery(1, new CapturedMessage(new byte[] { 1 }, new TechnicalMetadata(null!, "", "", null, null, DateTimeOffset.UtcNow))),
                    new BufferedDelivery(2, new CapturedMessage(new byte[] { 2 }, new TechnicalMetadata(null!, "", "", null, null, DateTimeOffset.UtcNow)))
                });

            var method = typeof(RabbitMqConsumer).GetMethod("AckBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var task = (Task)method.Invoke(consumer, new object[] { batch, CancellationToken.None })!;
            await task.ConfigureAwait(false);

            channel.Verify(c => c.BasicAckAsync(1, false, It.IsAny<CancellationToken>()), Times.Once);
            channel.Verify(c => c.BasicAckAsync(2, false, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        [Category("Contract")]
        public async Task RequeueBatchAsync_InvokesBasicNackForEachDelivery()
        {
            var consumer = CreateMinimalConsumer();

            var channel = new Mock<IChannel>();
            channel
                .Setup(c => c.BasicNackAsync(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var inFlightField = typeof(RabbitMqConsumer).GetField("inFlight", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(inFlightField, Is.Not.Null);
            var inFlight = (ConcurrentDictionary<ulong, BufferedDelivery>)inFlightField.GetValue(consumer)!;
            inFlight[7] = new BufferedDelivery(7, new CapturedMessage(new byte[] { 7 }, new TechnicalMetadata(null!, "", "", null, null, DateTimeOffset.UtcNow)));

            var channelField = typeof(RabbitMqConsumer).GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(channelField, Is.Not.Null);
            channelField.SetValue(consumer, channel.Object);

            var batch = new CaptureBatch(
                BatchId: Guid.NewGuid().ToString(),
                CreatedAtUtc: DateTimeOffset.UtcNow,
                TotalBytes: 1,
                Deliveries: new List<BufferedDelivery>
                {
                    new BufferedDelivery(7, new CapturedMessage(new byte[] { 7 }, new TechnicalMetadata(null!, "", "", null, null, DateTimeOffset.UtcNow)))
                });

            var method = typeof(RabbitMqConsumer).GetMethod("RequeueBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var task = (Task)method.Invoke(consumer, new object[] { batch, CancellationToken.None })!;
            await task.ConfigureAwait(false);

            channel.Verify(c => c.BasicNackAsync(7, false, true, It.IsAny<CancellationToken>()), Times.Once);
        }

        private static RabbitMqConsumer CreateMinimalConsumer()
        {
            var options = new RabbitMqConsumerOptions(null)
            {
                HostName = "localhost",
                QueueName = "q-test"
            };

            var batchBuffer = new CaptureBatchBuffer(
                Options.Create(new CaptureBatchBufferOptions()),
                new TestLogger<CaptureBatchBuffer>());

            var storageWriter = new Mock<IStorageWriter>();
            storageWriter
                .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(StorageWriteResult.SuccessResult("fake://location"));

            var telemetry = new Mock<IRabbitMqConsumerTelemetry>();
            telemetry.Setup(t => t.SetQueueLag(It.IsAny<int>()));

            return new RabbitMqConsumer(
                Options.Create(options),
                batchBuffer,
                storageWriter.Object,
                telemetry.Object,
                new TestLogger<RabbitMqConsumer>());
        }
    }
}
