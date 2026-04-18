namespace Danaid.Core.Capture;

public sealed record CapturedMessage(
    byte[] Body,
    IDictionary<string, object?>? Headers,
    string RoutingKey,
    string Exchange,
    string? CorrelationId,
    string? MessageId,
    DateTimeOffset TimestampUtc
);
