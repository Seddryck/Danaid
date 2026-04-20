using System.Text;
using Danaid.Core.Capture;
using RabbitMQ.Client.Events;

namespace Danaid.Core.Consumption;

public static class RabbitMqDeliveryMapper
{
    public static CapturedMessage ToCapturedMessage(BasicDeliverEventArgs args)
    {
        var headers = NormalizeHeaders(args.BasicProperties.Headers);

        return new CapturedMessage(
            Body: args.Body.ToArray(),
            TechnicalMetadata: new TechnicalMetadata(
                Headers: headers,
                RoutingKey: args.RoutingKey,
                Exchange: args.Exchange,
                CorrelationId: args.BasicProperties.CorrelationId,
                MessageId: args.BasicProperties.MessageId,
                CaptureTimestampUtc: DateTimeOffset.UtcNow));
    }

    private static IDictionary<string, object?>? NormalizeHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null)
            return null;

        var normalized = new Dictionary<string, object?>(headers.Count, StringComparer.Ordinal);

        foreach (var (key, value) in headers)
            normalized[key] = NormalizeValue(value);

        return normalized;
    }

    private static object? NormalizeValue(object? value)
        => value switch
        {
            null => null,
            byte[] bytes => TryUtf8(bytes, out var utf8) ? utf8 : Convert.ToBase64String(bytes),
            ReadOnlyMemory<byte> memory => TryUtf8(memory.Span, out var utf8) ? utf8 : Convert.ToBase64String(memory.ToArray()),
            IReadOnlyList<object?> list => list.Select(NormalizeValue).ToArray(),
            string or bool or char or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or DateTime or DateTimeOffset or Guid => value,
            _ => value.ToString()
        };

    private static bool TryUtf8(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            text = Encoding.UTF8.GetString(bytes);
            return Encoding.UTF8.GetByteCount(text) == bytes.Length;
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }
}
