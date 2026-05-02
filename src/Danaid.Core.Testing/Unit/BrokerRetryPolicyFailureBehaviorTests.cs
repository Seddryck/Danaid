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
    public class BrokerRetryPolicyFailureBehaviorTests
    {
        [Test]
        [Category("Resilience")]
        public async Task Policy_ThrowsAfterConfiguredRetries()
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

            // Operation always fails with IOException
            async Task Operation(CancellationToken ct)
            {
                attempts++;
                await Task.Yield();
                throw new IOException("cannot connect");
            }

            var ex = Assert.ThrowsAsync<IOException>(async () => await policy.ExecuteAsync(async ct => await Operation(ct), CancellationToken.None));

            Assert.That(ex, Is.Not.Null);
            // default retryCount is 3 in the factory => total attempts = initial + retries = 4
            Assert.That(attempts, Is.EqualTo(4));
            Assert.That(ex!.Message, Does.Contain("cannot connect"));
        }
    }
}
