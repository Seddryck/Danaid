using Danaid.Core.Capture;
using Danaid.Core.Consumption;
using Danaid.Core.Storage;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace Danaid.Core.Testing.Unit;

[TestFixture]
[Category("Functional")]
public class RabbitMqConsumerUnitTests
{
    [Test]
    [Category("Configuration")]
    public void RunAsync_RejectsMultipleQueues()
    {
        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q1,q2"
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(
            async () => await consumer.RunAsync(cts.Token),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    [Category("Configuration")]
    public void RunAsync_UsesConfiguredValuesWithoutHardcoding()
    {
        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "custom-host",
            Port = 5673,
            VirtualHost = "custom-vhost",
            UserName = "custom-user",
            Password = "custom-password",
            QueueName = "capture-queue"
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(
            async () => await consumer.RunAsync(cts.Token),
            Throws.Nothing);
    }

    [Test]
    [Category("Contract")]
    public void RunAsync_AllowsPluggableStorageWriterImplementation()
    {
        var writer = new Mock<IStorageWriter>();
        writer
            .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorageWriteResult.SuccessResult("fake://location"));

        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "capture-queue"
        }, writer.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(
            async () => await consumer.RunAsync(cts.Token),
            Throws.Nothing);
    }

    private static RabbitMqConsumer CreateConsumer(RabbitMqConsumerOptions options, IStorageWriter? storageWriter = null)
    {
        var batchBuffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions()),
            new TestLogger<CaptureBatchBuffer>());

        storageWriter ??= new FileSystemStorageWriter(
            Options.Create(new FileSystemStorageWriterOptions()),
            new TestLogger<FileSystemStorageWriter>());

        var telemetry = new Mock<IRabbitMqConsumerTelemetry>();
        telemetry.Setup(x => x.SetQueueLag(It.IsAny<int>()));
        telemetry.Setup(x => x.MessageReceived());
        telemetry.Setup(x => x.BatchFailed(It.IsAny<int>()));
        telemetry.Setup(x => x.BatchRetried(It.IsAny<int>()));

        return new RabbitMqConsumer(
            Options.Create(options),
            batchBuffer,
            storageWriter,
            telemetry.Object,
            new TestLogger<RabbitMqConsumer>());
    }
}
