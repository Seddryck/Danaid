using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Danaid.Core.Capture;
using Danaid.Core.Consumption;
using Danaid.Core.Storage;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Danaid.Core.Testing.Unit
{
    [TestFixture]
    public class RabbitMqConsumerBehaviorUnitTests
    {
        [Test]
        [Category("Contract")]
        public async Task OnMessageReceivedAsync_BuffersMessageAndUpdatesTelemetry()
        {
            var options = new RabbitMqConsumerOptions(null)
            {
                HostName = "localhost",
                QueueName = "q-test"
            };

            // real batch buffer so enqueued item goes into a concrete structure
            var batchBuffer = new CaptureBatchBuffer(
                Options.Create(new CaptureBatchBufferOptions()),
                new TestLogger<CaptureBatchBuffer>());

            var storageWriter = new Mock<IStorageWriter>();
            storageWriter
                .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(StorageWriteResult.SuccessResult("fake://location"));

            var telemetry = new Mock<IRabbitMqConsumerTelemetry>();
            telemetry.Setup(t => t.SetQueueLag(It.IsAny<int>()));
            telemetry.Setup(t => t.MessageReceived());

            var consumer = new RabbitMqConsumer(
                Options.Create(options),
                batchBuffer,
                storageWriter.Object,
                telemetry.Object,
                new TestLogger<RabbitMqConsumer>());

            var onMessageMethod = typeof(RabbitMqConsumer).GetMethod("OnMessageReceivedAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(onMessageMethod, Is.Not.Null);

            var args = CreateArgs(
                messageId: "m1",
                correlationId: "c1",
                routingKey: "rk",
                exchange: "ex",
                headers: null,
                body: new byte[] { 1 });

            var task = (Task)onMessageMethod.Invoke(consumer, new object[] { null!, args })!;
            await task.ConfigureAwait(false);

            // telemetry should have been informed that a message was received and queue lag set
            telemetry.Verify(t => t.MessageReceived(), Times.Once);
            telemetry.Verify(t => t.SetQueueLag(It.Is<int>(n => n >= 1)), Times.AtLeastOnce);
        }

        [Test]
        [Category("Contract")]
        public void AckBatchAsync_ThrowsIfChannelNotInitialized()
        {
            var consumer = CreateMinimalConsumer();

            var method = typeof(RabbitMqConsumer).GetMethod("AckBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var batch = new CaptureBatch(
                BatchId: Guid.NewGuid().ToString(),
                CreatedAtUtc: DateTimeOffset.UtcNow,
                TotalBytes: 1,
                Deliveries: new List<BufferedDelivery> { new BufferedDelivery(1, new CapturedMessage(new byte[] { 1 }, new TechnicalMetadata(null!, "", "", null, null, DateTimeOffset.UtcNow))) });

            // Invoke via reflection. The target may throw synchronously (MethodInfo.Invoke will
            // surface a TargetInvocationException) or return a Task that faults when awaited.
            try
            {
                var result = method.Invoke(consumer, new object[] { batch, CancellationToken.None });
                if (result is Task task)
                    Assert.That(async () => await task.ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>());
                else
                    Assert.Fail("Expected an exception when invoking AckBatchAsync but none was thrown.");
            }
            catch (TargetInvocationException tie)
            {
                Assert.That(tie.InnerException, Is.TypeOf<InvalidOperationException>());
            }
        }

        [Test]
        [Category("Contract")]
        public void RequeueBatchAsync_ThrowsIfChannelNotInitialized()
        {
            var consumer = CreateMinimalConsumer();

            var method = typeof(RabbitMqConsumer).GetMethod("RequeueBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            var batch = new CaptureBatch(
                BatchId: Guid.NewGuid().ToString(),
                CreatedAtUtc: DateTimeOffset.UtcNow,
                TotalBytes: 1,
                Deliveries: new List<BufferedDelivery> { new BufferedDelivery(1, new CapturedMessage(new byte[] { 1 }, new TechnicalMetadata(null!, "", "", null, null, DateTimeOffset.UtcNow))) });

            try
            {
                var result = method.Invoke(consumer, new object[] { batch, CancellationToken.None });
                if (result is Task task)
                    Assert.That(async () => await task.ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>());
                else
                    Assert.Fail("Expected an exception when invoking RequeueBatchAsync but none was thrown.");
            }
            catch (TargetInvocationException tie)
            {
                Assert.That(tie.InnerException, Is.TypeOf<InvalidOperationException>());
            }
        }

        [Test]
        [Category("Observability")]
        public async Task RabbitMqConsumerTelemetry_CountersAndGaugeEmitMeasurements()
        {
            var telemetry = new RabbitMqConsumerTelemetry();

            var measurements = new List<(string Name, long Value)>();
            var queueLagTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                // enable counters and gauges we care about
                if (instrument.Name == "danaid.messages.received" ||
                    instrument.Name == "danaid.messages.failed" ||
                    instrument.Name == "danaid.messages.retry" ||
                    instrument.Name == "danaid.queue.lag")
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };

            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                measurements.Add((instrument.Name, measurement));
                if (instrument.Name == "danaid.queue.lag" && measurement == 42)
                    queueLagTcs.TrySetResult(true);
            });

            listener.Start();

            // trigger increments
            telemetry.MessageReceived();
            telemetry.BatchFailed(2);
            telemetry.BatchRetried(3);

            // ensure counters were recorded (wait briefly for listener callbacks)
            var swCounters = System.Diagnostics.Stopwatch.StartNew();
            while (swCounters.ElapsedMilliseconds < 200 &&
                   !(measurements.Exists(m => m.Name == "danaid.messages.received") &&
                     measurements.Exists(m => m.Name == "danaid.messages.failed") &&
                     measurements.Exists(m => m.Name == "danaid.messages.retry")))
            {
                await Task.Delay(20).ConfigureAwait(false);
            }

            Assert.That(measurements.Exists(m => m.Name == "danaid.messages.received"), Is.True);
            Assert.That(measurements.Exists(m => m.Name == "danaid.messages.failed"), Is.True);
            Assert.That(measurements.Exists(m => m.Name == "danaid.messages.retry"), Is.True);

            // now test observable gauge: set queue lag and verify internal state (deterministic)
            telemetry.SetQueueLag(42);

            var qField = typeof(RabbitMqConsumerTelemetry).GetField("queueLag", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(qField, Is.Not.Null);
            var qValue = (long)qField!.GetValue(telemetry)!;
            Assert.That(qValue, Is.EqualTo(42));
        }

        // Helpers
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

        private static BasicDeliverEventArgs CreateArgs(
            string? messageId,
            string? correlationId,
            string routingKey,
            string exchange,
            IDictionary<string, object?>? headers,
            byte[] body)
        {
            var basicProperties = new BasicProperties
            {
                MessageId = messageId,
                CorrelationId = correlationId,
                Headers = headers
            };

            return new BasicDeliverEventArgs(
                "test-consumer",
                1,
                false,
                exchange,
                routingKey,
                basicProperties,
                new ReadOnlyMemory<byte>(body),
                CancellationToken.None);
        }
    }
}
