using Polly;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Exceptions;
using System.IO;

namespace Danaid.Core.Consumption;

public sealed class DefaultBrokerRetryPolicyFactory : IBrokerRetryPolicyFactory
{
    public AsyncPolicy Create(RabbitMqConsumerOptions options, ILogger<RabbitMqConsumer> logger)
    {
        return Policy
            .Handle<BrokerUnreachableException>()
            .Or<AlreadyClosedException>()
            .Or<IOException>()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: _ => options.ReconnectDelay,
                onRetry: (exception, delay, attempt, _) =>
                {
                    logger.LogWarning(
                        exception,
                        "Transient broker failure while establishing RabbitMQ consumption. Queue={QueueName} Attempt={Attempt} Delay={Delay}",
                        options.QueueName,
                        attempt,
                        delay);
                });
    }
}
