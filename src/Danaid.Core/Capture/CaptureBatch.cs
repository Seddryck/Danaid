namespace Danaid.Core.Capture;

public sealed record CaptureBatch(
    string BatchId,
    DateTimeOffset CreatedAtUtc,
    long TotalBytes,
    IReadOnlyList<BufferedDelivery> Deliveries
);
