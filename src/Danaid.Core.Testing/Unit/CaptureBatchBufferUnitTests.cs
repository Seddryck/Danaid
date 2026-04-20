using Danaid.Core.Capture;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Danaid.Core.Testing.Unit;

[TestFixture]
[Category("Functional")]
[Category("Configuration")]
public class CaptureBatchBufferUnitTests
{
    [Test]
    public async Task EnqueueAsync_WithBoundedBuffer_BlocksWhenCapacityReached()
    {
        var buffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions
            {
                Capacity = 1,
                MaxCount = 100,
                MaxBytes = 10_000,
                MaxWait = TimeSpan.FromMinutes(1)
            }),
            new TestLogger<CaptureBatchBuffer>());

        await buffer.EnqueueAsync(CreateDelivery(1, "first"), CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        Assert.That(
            async () => await buffer.EnqueueAsync(CreateDelivery(2, "second"), cts.Token),
            Throws.TypeOf<OperationCanceledException>());
    }

    private static BufferedDelivery CreateDelivery(ulong tag, string messageId)
        => new(tag, new CapturedMessage(
            body: [1],
            headers: new Dictionary<string, object?> { ["h"] = "v" },
            routingKey: "rk",
            exchange: "ex",
            correlationId: "corr",
            messageId: messageId,
            timestampUtc: DateTimeOffset.UtcNow));
}
