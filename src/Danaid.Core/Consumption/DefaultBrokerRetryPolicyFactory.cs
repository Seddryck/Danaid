using Polly;
using Microsoft.Extensions.Logging;

namespace Danaid.Core.Consumption;

public sealed class DefaultBrokerRetryPolicyFactory : IBrokerRetryPolicyFactory
{
    public AsyncPolicy Create(RabbitMqConsumerOptions options, ILogger<RabbitMqConsumer> logger)
        => RabbitMqConsumer.CreateDefaultBrokerRetryPolicy(options, logger);
}
