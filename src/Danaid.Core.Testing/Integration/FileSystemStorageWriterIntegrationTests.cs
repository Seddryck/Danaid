using System.Text.Json;
using Danaid.Core.Capture;
using Danaid.Core.Storage;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Danaid.Core.Testing.Integration;

[TestFixture]
[Category("Functional")]
public class FileSystemStorageWriterIntegrationTests
{
    [Test]
    [Category("Serialization")]
    [Category("Contract")]
    public async Task MetadataPersistence_PreservesHeadersRoutingAndIdentifiers()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var writer = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = tempPath, MaxRetries = 0 }),
            new TestLogger<FileSystemStorageWriter>());

        var batch = new CaptureBatch(
            "batch-meta",
            DateTimeOffset.UtcNow,
            3,
            new[]
            {
                new BufferedDelivery(101, new CapturedMessage(
                    Body: [1, 2, 3],
                    Headers: new Dictionary<string, object?> { ["h1"] = "v1" },
                    RoutingKey: "rk.orders",
                    Exchange: "ex.capture",
                    CorrelationId: "corr-1",
                    MessageId: "msg-1",
                    TimestampUtc: DateTimeOffset.UtcNow))
            });

        var result = await writer.WriteAsync(batch, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.Location, Is.Not.Null);

        var json = await File.ReadAllTextAsync(result.Location!);
        using var document = JsonDocument.Parse(json);

        var message = document.RootElement.EnumerateArray().Single();
        Assert.Multiple(() =>
        {
            Assert.That(message.GetProperty("MessageId").GetString(), Is.EqualTo("msg-1"));
            Assert.That(message.GetProperty("CorrelationId").GetString(), Is.EqualTo("corr-1"));
            Assert.That(message.GetProperty("RoutingKey").GetString(), Is.EqualTo("rk.orders"));
            Assert.That(message.GetProperty("Exchange").GetString(), Is.EqualTo("ex.capture"));
            Assert.That(message.GetProperty("Headers").GetProperty("h1").GetString(), Is.EqualTo("v1"));
        });
    }

    [Test]
    [Category("Serialization")]
    [Category("Contract")]
    public async Task DuplicateHandling_PersistsDuplicateStableIdentifiers()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var writer = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = tempPath, MaxRetries = 0 }),
            new TestLogger<FileSystemStorageWriter>());

        var duplicateId = "msg-duplicate";
        var deliveries = new[]
        {
            CreateDelivery(1, duplicateId),
            CreateDelivery(2, duplicateId)
        };

        var batch = new CaptureBatch("batch-dup", DateTimeOffset.UtcNow, 2, deliveries);
        var result = await writer.WriteAsync(batch, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error);

        var json = await File.ReadAllTextAsync(result.Location!);
        using var document = JsonDocument.Parse(json);

        var ids = document.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("MessageId").GetString())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(ids.Length, Is.EqualTo(2));
            Assert.That(ids.All(x => x == duplicateId), Is.True);
        });
    }

    private static BufferedDelivery CreateDelivery(ulong tag, string messageId)
        => new(tag, new CapturedMessage(
            Body: [1],
            Headers: new Dictionary<string, object?> { ["h"] = "v" },
            RoutingKey: "rk",
            Exchange: "ex",
            CorrelationId: "corr",
            MessageId: messageId,
            TimestampUtc: DateTimeOffset.UtcNow));
}
