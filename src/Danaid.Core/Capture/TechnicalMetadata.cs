namespace Danaid.Core.Capture;

public sealed record TechnicalMetadata(
    IDictionary<string, object?>? Headers,
    string RoutingKey,
    string Exchange,
    string? CorrelationId,
    string? MessageId,
    DateTimeOffset CaptureTimestampUtc
);
