namespace Danaid.Core.Capture;

public sealed record CapturedMessage(
    byte[] Body,
    TechnicalMetadata TechnicalMetadata)
{
    public IDictionary<string, object?>? Headers => TechnicalMetadata.Headers;

    public string RoutingKey => TechnicalMetadata.RoutingKey;

    public string Exchange => TechnicalMetadata.Exchange;

    public string? CorrelationId => TechnicalMetadata.CorrelationId;

    public string? MessageId => TechnicalMetadata.MessageId;

    public DateTimeOffset TimestampUtc => TechnicalMetadata.CaptureTimestampUtc;

    public CapturedMessage(
        byte[] body,
        IDictionary<string, object?>? headers,
        string routingKey,
        string exchange,
        string? correlationId,
        string? messageId,
        DateTimeOffset timestampUtc)
        : this(
            body,
            new TechnicalMetadata(
                headers,
                routingKey,
                exchange,
                correlationId,
                messageId,
                timestampUtc))
    {
    }
}
