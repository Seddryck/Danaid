namespace Danaid.Core;

public sealed record RabbitMqConsumerOptions(string? ConsumerTag)
{
    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string VirtualHost { get; init; } = "/";
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string QueueName { get; init; } = string.Empty;
    public ushort PrefetchCount { get; init; } = 100;
    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(3);
}
