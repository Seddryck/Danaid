using System;
using System.Reflection;
using System.Threading;
using Danaid.Core.Consumption;
using Danaid.Core.Capture;
using Danaid.Core.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Polly;

namespace Danaid.Core.Testing.Unit
{
    [TestFixture]
    public class BrokerPolicyInjectionUnitTests
    {
        [Test]
        [Category("Resilience")]
        public void Constructor_AssignsInjectedBrokerPolicy()
        {
            var options = new RabbitMqConsumerOptions(null)
            {
                HostName = "localhost",
                QueueName = "q-test"
            };

            var batchBuffer = new CaptureBatchBuffer(Options.Create(new CaptureBatchBufferOptions()), new TestLogger<CaptureBatchBuffer>());
            var storageWriter = new Mock<Danaid.Core.Storage.IStorageWriter>();
            var telemetry = new Mock<IRabbitMqConsumerTelemetry>();
            var logger = new Mock<ILogger<RabbitMqConsumer>>();

            var noOp = Policy.NoOpAsync();

            var consumer = new RabbitMqConsumer(Options.Create(options), batchBuffer, storageWriter.Object, telemetry.Object, logger.Object, brokerRetryPolicy: noOp);

            var policyField = typeof(RabbitMqConsumer).GetField("brokerRetryPolicy", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(policyField, Is.Not.Null);

            var fieldValue = policyField.GetValue(consumer);
            Assert.That(fieldValue, Is.SameAs(noOp));
        }

        [Test]
        [Category("Resilience")]
        public void Constructor_UsesFactoryWhenProvided()
        {
            var options = new RabbitMqConsumerOptions(null)
            {
                HostName = "localhost",
                QueueName = "q-test"
            };

            var batchBuffer = new CaptureBatchBuffer(Options.Create(new CaptureBatchBufferOptions()), new TestLogger<CaptureBatchBuffer>());
            var storageWriter = new Mock<Storage.IStorageWriter>();
            var telemetry = new Mock<IRabbitMqConsumerTelemetry>();
            var logger = new Mock<ILogger<RabbitMqConsumer>>();

            var factory = new Mock<IBrokerRetryPolicyFactory>();
            var createdPolicy = Policy.NoOpAsync();
            factory.Setup(f => f.Create(It.IsAny<RabbitMqConsumerOptions>(), It.IsAny<ILogger<RabbitMqConsumer>>()))
                .Returns(createdPolicy)
                .Verifiable();

            var consumer = new RabbitMqConsumer(Options.Create(options), batchBuffer, storageWriter.Object, telemetry.Object, logger.Object, brokerRetryPolicyFactory: factory.Object);

            factory.Verify(f => f.Create(It.IsAny<RabbitMqConsumerOptions>(), It.IsAny<ILogger<RabbitMqConsumer>>()), Times.Once);

            var policyField = typeof(RabbitMqConsumer).GetField("brokerRetryPolicy", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(policyField, Is.Not.Null);

            var fieldValue = policyField.GetValue(consumer);
            Assert.That(fieldValue, Is.SameAs(createdPolicy));
        }
    }
}
