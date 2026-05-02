using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Danaid.Core.Consumption;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Polly;
using RabbitMQ.Client.Exceptions;

namespace Danaid.Core.Testing.Unit
{
    [TestFixture]
    public class RabbitMqBrokerRetryPolicyTests
    {
        [Test]
        [Category("Resilience")]
        public async Task BrokerRetryPolicy_RetriesOnTransientBrokerExceptions()
        {
            var options = new RabbitMqConsumerOptions(null)
            {
                HostName = "localhost",
                QueueName = "q-test",
                // keep retries fast for test
                ReconnectDelay = TimeSpan.Zero
            };

            var batchBuffer = new Mock<object>(); // placeholder - not used for this test

            var storageWriter = new Mock<object>();

            var logger = new Mock<ILogger<RabbitMqConsumer>>();

            // create the default policy via the default factory implementation
            var factory = new DefaultBrokerRetryPolicyFactory();
            var policy = factory.Create(options, logger.Object);
            Assert.That(policy, Is.Not.Null);

            var attempts = 0;

            // simulate an operation that fails twice with IOException then succeeds
            async Task Operation(CancellationToken ct)
            {
                attempts++;
                if (attempts <= 2)
                    throw new System.IO.IOException("transient");

                await Task.CompletedTask;
            }

            // execute the policy; it should retry and eventually succeed
            await policy.ExecuteAsync(async ct => await Operation(ct), CancellationToken.None).ConfigureAwait(false);

            // attempts should be 3 (2 failures + 1 success)
            Assert.That(attempts, Is.EqualTo(3));

            // logger warning is emitted by the policy on retry; we don't assert extension method invocations here
        }
    }
}
