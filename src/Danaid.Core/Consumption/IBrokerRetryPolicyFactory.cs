using Polly;
using Microsoft.Extensions.Logging;

namespace Danaid.Core.Consumption;

public interface IBrokerRetryPolicyFactory
{
    AsyncPolicy Create(RabbitMqConsumerOptions options, ILogger<RabbitMqConsumer> logger);
}
