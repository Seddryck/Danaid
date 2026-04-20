using Danaid.Core.Consumption;
using System.Text;
using NUnit.Framework;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Danaid.Core.Testing.Unit;

[TestFixture]
[Category("Functional")]
public class RabbitMqDeliveryMapperUnitTests
{
    [Test]
    [Category("Contract")]
    public void ToCapturedMessage_MapsMessageIdentifier_WhenPresent()
    {
        var args = CreateArgs(
            messageId: "msg-1",
            correlationId: "corr-1",
            routingKey: "rk",
            exchange: "ex",
            headers: null,
            body: [1]);

        var message = RabbitMqDeliveryMapper.ToCapturedMessage(args);

        Assert.That(message.MessageId, Is.EqualTo("msg-1"));
    }

    [Test]
    [Category("Contract")]
    public void ToCapturedMessage_MapsCorrelationIdentifier_WhenPresent()
    {
        var args = CreateArgs(
            messageId: "msg-2",
            correlationId: "corr-2",
            routingKey: "rk",
            exchange: "ex",
            headers: null,
            body: [2]);

        var message = RabbitMqDeliveryMapper.ToCapturedMessage(args);

        Assert.That(message.CorrelationId, Is.EqualTo("corr-2"));
    }

    [Test]
    [Category("Contract")]
    public void ToCapturedMessage_MapsRoutingKeyAndExchange()
    {
        var args = CreateArgs(
            messageId: "msg-3",
            correlationId: "corr-3",
            routingKey: "rk.orders",
            exchange: "ex.capture",
            headers: null,
            body: [3]);

        var message = RabbitMqDeliveryMapper.ToCapturedMessage(args);

        Assert.Multiple(() =>
        {
            Assert.That(message.RoutingKey, Is.EqualTo("rk.orders"));
            Assert.That(message.Exchange, Is.EqualTo("ex.capture"));
        });
    }

    [Test]
    [Category("Serialization")]
    public void ToCapturedMessage_NormalizesHeadersIntoStorableValues()
    {
        var args = CreateArgs(
            messageId: "msg-4",
            correlationId: "corr-4",
            routingKey: "rk",
            exchange: "ex",
            headers: new Dictionary<string, object?>
            {
                ["text"] = Encoding.UTF8.GetBytes("value"),
                ["number"] = 7,
                ["flag"] = true
            },
            body: [4]);

        var message = RabbitMqDeliveryMapper.ToCapturedMessage(args);

        Assert.Multiple(() =>
        {
            Assert.That(message.Headers, Is.Not.Null);
            Assert.That(message.Headers!["text"], Is.EqualTo("value"));
            Assert.That(message.Headers!["number"], Is.EqualTo(7));
            Assert.That(message.Headers!["flag"], Is.EqualTo(true));
        });
    }

    [Test]
    [Category("Contract")]
    public void ToCapturedMessage_HandlesMissingOptionalFields()
    {
        var args = CreateArgs(
            messageId: null,
            correlationId: null,
            routingKey: "rk",
            exchange: "ex",
            headers: null,
            body: [5]);

        var message = RabbitMqDeliveryMapper.ToCapturedMessage(args);

        Assert.Multiple(() =>
        {
            Assert.That(message.MessageId, Is.Null);
            Assert.That(message.CorrelationId, Is.Null);
            Assert.That(message.Headers, Is.Null);
        });
    }

    [Test]
    [Category("Contract")]
    public void ToCapturedMessage_SetsCaptureTimestampInUtc()
    {
        var before = DateTimeOffset.UtcNow;

        var message = RabbitMqDeliveryMapper.ToCapturedMessage(CreateArgs(
            messageId: "msg-6",
            correlationId: "corr-6",
            routingKey: "rk",
            exchange: "ex",
            headers: null,
            body: [6]));

        var after = DateTimeOffset.UtcNow;

        Assert.Multiple(() =>
        {
            Assert.That(message.TimestampUtc.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(message.TimestampUtc, Is.GreaterThanOrEqualTo(before));
            Assert.That(message.TimestampUtc, Is.LessThanOrEqualTo(after));
        });
    }

    [Test]
    [Category("Contract")]
    public void ToCapturedMessage_KeepsTechnicalMetadataSeparateFromPayload()
    {
        var body = new byte[] { 11, 22, 33 };
        var message = RabbitMqDeliveryMapper.ToCapturedMessage(CreateArgs(
            messageId: "msg-7",
            correlationId: "corr-7",
            routingKey: "rk",
            exchange: "ex",
            headers: new Dictionary<string, object?> { ["h"] = "v" },
            body: body));

        Assert.Multiple(() =>
        {
            Assert.That(message.Body, Is.EqualTo(body));
            Assert.That(message.TechnicalMetadata, Is.Not.Null);
            Assert.That(message.TechnicalMetadata.MessageId, Is.EqualTo("msg-7"));
        });
    }

    private static BasicDeliverEventArgs CreateArgs(
        string? messageId,
        string? correlationId,
        string routingKey,
        string exchange,
        IDictionary<string, object?>? headers,
        byte[] body)
    {
        var basicProperties = new BasicProperties
        {
            MessageId = messageId,
            CorrelationId = correlationId,
            Headers = headers
        };

        return new BasicDeliverEventArgs(
            "test-consumer",
            1,
            false,
            exchange,
            routingKey,
            basicProperties,
            new ReadOnlyMemory<byte>(body),
            CancellationToken.None);
    }
}
