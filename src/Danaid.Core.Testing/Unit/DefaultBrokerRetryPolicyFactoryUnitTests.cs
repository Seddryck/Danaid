using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Danaid.Core.Consumption;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Danaid.Core.Testing.Unit
{
    [TestFixture]
    public class DefaultBrokerRetryPolicyFactoryUnitTests
    {
        [Test]
        [Category("Resilience")]
        public async Task Create_RetriesOnTransientBrokerExceptions()
        {
            var options = new RabbitMqConsumerOptions(null)
            {
                HostName = "localhost",
                QueueName = "q-test",
                ReconnectDelay = TimeSpan.Zero
            };

            var logger = new Mock<ILogger<RabbitMqConsumer>>();

            var factory = new DefaultBrokerRetryPolicyFactory();
            var policy = factory.Create(options, logger.Object);

            var attempts = 0;

            async Task Operation(CancellationToken ct)
            {
                attempts++;
                if (attempts <= 2)
                    throw new IOException("transient");

                await Task.CompletedTask;
            }

            await policy.ExecuteAsync(async ct => await Operation(ct), CancellationToken.None).ConfigureAwait(false);

            Assert.That(attempts, Is.EqualTo(3));
        }

        [Test]
        [Category("Resilience")]
        public async Task Create_ThrowsAfterConfiguredRetries()
        {
            var options = new RabbitMqConsumerOptions(null)
            {
                HostName = "localhost",
                QueueName = "q-test",
                ReconnectDelay = TimeSpan.Zero
            };

            var logger = new Mock<ILogger<RabbitMqConsumer>>();

            var factory = new DefaultBrokerRetryPolicyFactory();
            var policy = factory.Create(options, logger.Object);

            var attempts = 0;

            async Task Operation(CancellationToken ct)
            {
                attempts++;
                await Task.Yield();
                throw new IOException("cannot connect");
            }

            var ex = Assert.ThrowsAsync<IOException>(async () => await policy.ExecuteAsync(async ct => await Operation(ct), CancellationToken.None));

            Assert.That(ex, Is.Not.Null);
            Assert.That(attempts, Is.EqualTo(4));
            Assert.That(ex!.Message, Does.Contain("cannot connect"));
        }
    }
}
