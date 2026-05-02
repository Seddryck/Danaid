using System;
using System.Threading;
using System.Threading.Tasks;
using Danaid.Core.Capture;
using Danaid.Core.Consumption;
using Danaid.Core.Storage;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Danaid.Core.Testing.Unit
{
    [TestFixture]
    public class BrokerRunAsyncUnitTests
    {
        [Test]
        [Category("Configuration")]
        public void RunAsync_Throws_WhenOptionsInvalid()
        {
            var options = new RabbitMqConsumerOptions(null)
            {
                HostName = "localhost",
                QueueName = "" // invalid
            };

            var batchBuffer = new CaptureBatchBuffer(Options.Create(new CaptureBatchBufferOptions()), new TestLogger<CaptureBatchBuffer>());
            var storageWriter = new Mock<IStorageWriter>();
            var telemetry = new Mock<IRabbitMqConsumerTelemetry>();

            var consumer = new RabbitMqConsumer(Options.Create(options), batchBuffer, storageWriter.Object, telemetry.Object, new TestLogger<RabbitMqConsumer>());

            Assert.ThrowsAsync<InvalidOperationException>(async () => await consumer.RunAsync(CancellationToken.None));
        }

        [Test]
        [Category("Resilience")]
        public async Task RunAsync_ReturnsImmediately_WhenCancellationRequested()
        {
            var options = new RabbitMqConsumerOptions(null)
            {
                HostName = "localhost",
                QueueName = "q-test"
            };

            var batchBuffer = new CaptureBatchBuffer(Options.Create(new CaptureBatchBufferOptions()), new TestLogger<CaptureBatchBuffer>());
            var storageWriter = new Mock<IStorageWriter>();
            var telemetry = new Mock<IRabbitMqConsumerTelemetry>();

            var consumer = new RabbitMqConsumer(Options.Create(options), batchBuffer, storageWriter.Object, telemetry.Object, new TestLogger<RabbitMqConsumer>());

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // cancel before calling RunAsync

            // should complete immediately and not throw
            Assert.DoesNotThrowAsync(async () => await consumer.RunAsync(cts.Token));
        }
    }
}
