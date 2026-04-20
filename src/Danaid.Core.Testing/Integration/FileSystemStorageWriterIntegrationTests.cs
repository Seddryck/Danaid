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
    [Category("Contract")]
    public async Task PersistedRecord_ContainsRawPayloadAndDistinctTechnicalMetadataSection()
    {
        var message = await PersistAndReadFirstMessageAsync(CreateBatch(CreateDelivery(101, [1, 2, 3], "msg-1", "corr-1")));

        Assert.Multiple(() =>
        {
            Assert.That(message.TryGetProperty("RawPayloadBase64", out _), Is.True);
            Assert.That(message.TryGetProperty("TechnicalMetadata", out _), Is.True);
        });
    }

    [Test]
    [Category("Contract")]
    public async Task PersistedTechnicalMetadata_IncludesTransportFields_WhenAvailable()
    {
        var captureTimestamp = DateTimeOffset.UtcNow;
        var delivery = new BufferedDelivery(
            101,
            new CapturedMessage(
                Body: [1, 2, 3],
                TechnicalMetadata: new TechnicalMetadata(
                    Headers: new Dictionary<string, object?> { ["h1"] = "v1" },
                    RoutingKey: "rk.orders",
                    Exchange: "ex.capture",
                    CorrelationId: "corr-1",
                    MessageId: "msg-1",
                    CaptureTimestampUtc: captureTimestamp)));

        var message = await PersistAndReadFirstMessageAsync(CreateBatch(delivery));
        var metadata = message.GetProperty("TechnicalMetadata");

        Assert.Multiple(() =>
        {
            Assert.That(metadata.GetProperty("MessageId").GetString(), Is.EqualTo("msg-1"));
            Assert.That(metadata.GetProperty("CorrelationId").GetString(), Is.EqualTo("corr-1"));
            Assert.That(metadata.GetProperty("RoutingKey").GetString(), Is.EqualTo("rk.orders"));
            Assert.That(metadata.GetProperty("Exchange").GetString(), Is.EqualTo("ex.capture"));
            Assert.That(metadata.GetProperty("Headers").GetProperty("h1").GetString(), Is.EqualTo("v1"));
            Assert.That(metadata.GetProperty("CaptureTimestampUtc").GetDateTimeOffset(), Is.EqualTo(captureTimestamp));
        });
    }

    [Test]
    [Category("Contract")]
    public async Task PersistedRecord_RemainsValid_WhenOptionalMetadataIsMissing()
    {
        var message = await PersistAndReadFirstMessageAsync(CreateBatch(CreateDelivery(12, [9], null, null)));
        var metadata = message.GetProperty("TechnicalMetadata");

        Assert.Multiple(() =>
        {
            Assert.That(message.GetProperty("RawPayloadBase64").GetString(), Is.EqualTo(Convert.ToBase64String([9])));
            Assert.That(metadata.GetProperty("MessageId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(metadata.GetProperty("CorrelationId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(metadata.TryGetProperty("Headers", out _), Is.True);
        });
    }

    [Test]
    [Category("Contract")]
    public async Task PersistedRecord_PreservesTraceAndCorrelationIdentifiers()
    {
        var message = await PersistAndReadFirstMessageAsync(CreateBatch(CreateDelivery(1, [1], "trace-id-1", "corr-id-1")));
        var metadata = message.GetProperty("TechnicalMetadata");

        Assert.Multiple(() =>
        {
            Assert.That(metadata.GetProperty("MessageId").GetString(), Is.EqualTo("trace-id-1"));
            Assert.That(metadata.GetProperty("CorrelationId").GetString(), Is.EqualTo("corr-id-1"));
        });
    }

    [Test]
    [Category("Serialization")]
    public async Task HeadersWithMixedTypes_AreSerializedPredictably()
    {
        var delivery = new BufferedDelivery(
            25,
            new CapturedMessage(
                Body: [1],
                TechnicalMetadata: new TechnicalMetadata(
                    Headers: new Dictionary<string, object?>
                    {
                        ["s"] = "value",
                        ["n"] = 42,
                        ["b"] = true,
                        ["bin"] = new byte[] { 1, 2, 3 }
                    },
                    RoutingKey: "rk",
                    Exchange: "ex",
                    CorrelationId: "corr",
                    MessageId: "msg",
                    CaptureTimestampUtc: DateTimeOffset.UtcNow)));

        var message = await PersistAndReadFirstMessageAsync(CreateBatch(delivery));
        var headers = message.GetProperty("TechnicalMetadata").GetProperty("Headers");

        Assert.Multiple(() =>
        {
            Assert.That(headers.GetProperty("s").GetString(), Is.EqualTo("value"));
            Assert.That(headers.GetProperty("n").GetInt32(), Is.EqualTo(42));
            Assert.That(headers.GetProperty("b").GetBoolean(), Is.True);
            Assert.That(headers.GetProperty("bin").GetString(), Is.EqualTo(Convert.ToBase64String([1, 2, 3])));
        });
    }

    [Test]
    [Category("Serialization")]
    public async Task PersistedTechnicalMetadata_CanBeReadBackWithoutIdentifierOrTimestampLoss()
    {
        var captureTimestamp = DateTimeOffset.UtcNow;
        var delivery = new BufferedDelivery(
            33,
            new CapturedMessage(
                Body: [2],
                TechnicalMetadata: new TechnicalMetadata(
                    Headers: new Dictionary<string, object?> { ["h"] = "v" },
                    RoutingKey: "rk",
                    Exchange: "ex",
                    CorrelationId: "corr-33",
                    MessageId: "msg-33",
                    CaptureTimestampUtc: captureTimestamp)));

        var message = await PersistAndReadFirstMessageAsync(CreateBatch(delivery));
        var metadata = message.GetProperty("TechnicalMetadata");

        Assert.Multiple(() =>
        {
            Assert.That(metadata.GetProperty("MessageId").GetString(), Is.EqualTo("msg-33"));
            Assert.That(metadata.GetProperty("CorrelationId").GetString(), Is.EqualTo("corr-33"));
            Assert.That(metadata.GetProperty("CaptureTimestampUtc").GetDateTimeOffset(), Is.EqualTo(captureTimestamp));
        });
    }

    [Test]
    [Category("Serialization")]
    public async Task CaptureTimestamp_IsSerializedAsUtc()
    {
        var message = await PersistAndReadFirstMessageAsync(CreateBatch(CreateDelivery(45, [7], "msg-45", "corr-45")));
        var captureTimestamp = message.GetProperty("TechnicalMetadata").GetProperty("CaptureTimestampUtc").GetDateTimeOffset();

        Assert.That(captureTimestamp.Offset, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    [Category("Contract")]
    public async Task PersistedMetadata_ExcludesConfiguredProviderInternalHeaders()
    {
        var delivery = new BufferedDelivery(
            46,
            new CapturedMessage(
                Body: [8],
                TechnicalMetadata: new TechnicalMetadata(
                    Headers: new Dictionary<string, object?>
                    {
                        ["x-death"] = "internal",
                        ["business-header"] = "value"
                    },
                    RoutingKey: "rk",
                    Exchange: "ex",
                    CorrelationId: "corr-46",
                    MessageId: "msg-46",
                    CaptureTimestampUtc: DateTimeOffset.UtcNow)));

        var message = await PersistAndReadFirstMessageAsync(CreateBatch(delivery));
        var headers = message.GetProperty("TechnicalMetadata").GetProperty("Headers");

        Assert.Multiple(() =>
        {
            Assert.That(headers.TryGetProperty("x-death", out _), Is.False);
            Assert.That(headers.GetProperty("business-header").GetString(), Is.EqualTo("value"));
        });
    }

    [Test]
    [Category("Contract")]
    public async Task WriteAsync_ReturnsSafeErrorCode_WhenPersistenceFails()
    {
        var writer = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = "bad|path", MaxRetries = 0, FailureErrorCode = "storage.write_failed" }),
            new TestLogger<FileSystemStorageWriter>());

        var result = await writer.WriteAsync(CreateBatch(CreateDelivery(47, [1], "msg-47", "corr-47")), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("storage.write_failed"));
        });
    }

    private static CaptureBatch CreateBatch(BufferedDelivery delivery)
        => new(
            "batch-meta",
            DateTimeOffset.UtcNow,
            delivery.Message.Body.Length,
            new[] { delivery });

    private static BufferedDelivery CreateDelivery(ulong tag, byte[] body, string? messageId, string? correlationId)
        => new(tag, new CapturedMessage(
            body: body,
            headers: new Dictionary<string, object?> { ["h"] = "v" },
            routingKey: "rk",
            exchange: "ex",
            correlationId: correlationId,
            messageId: messageId,
            timestampUtc: DateTimeOffset.UtcNow));

    private static async Task<JsonElement> PersistAndReadFirstMessageAsync(CaptureBatch batch)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var writer = new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions { BasePath = tempPath, MaxRetries = 0 }),
            new TestLogger<FileSystemStorageWriter>());

        var result = await writer.WriteAsync(batch, CancellationToken.None);

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(result.Location, Is.Not.Null);

        var json = await File.ReadAllTextAsync(result.Location!);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Single().Clone();
    }
}
