namespace Danaid.Core.Capture;

public sealed record BufferedDelivery(ulong DeliveryTag, CapturedMessage Message);
