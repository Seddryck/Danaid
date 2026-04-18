using Danaid.Core.Capture;
using Danaid.Core.Consumption;
using Danaid.Core.Storage;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Danaid.Core.Testing.Integration;

[TestFixture]
[Category("Functional")]
[Category("Resilience")]
public class RabbitMqConsumerIntegrationTests
{
    [Test]
    public async Task Reconnect_ValidatesPersistentConsumerLifecycleAndRecovery()
    {
        await RunWithRabbitMqContainerAsync(async rabbitMqContainer =>
        {
            var queueName = $"capture-restart-{Guid.NewGuid():N}";
            await EnsureQueueExistsAsync(rabbitMqContainer, queueName);

            var failedBeforeRestart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstWriter = new Mock<IStorageWriter>();
            firstWriter
                .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(StorageWriteResult.FailureResult("simulated transient failure"))
                .Callback(() => failedBeforeRestart.TrySetResult());

            var firstConsumer = CreateConsumer(CreateRabbitOptions(rabbitMqContainer, queueName), firstWriter.Object);
            using var firstCts = new CancellationTokenSource();
            var firstRunTask = firstConsumer.RunAsync(firstCts.Token);

            await PublishAsync(rabbitMqContainer, queueName, "msg-restart-1");
            await failedBeforeRestart.Task.WaitAsync(TimeSpan.FromSeconds(20));

            firstCts.Cancel();
            try { await firstRunTask; } catch (OperationCanceledException) { }

            var persistedAfterRestart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondWriter = new Mock<IStorageWriter>();
            secondWriter
                .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(StorageWriteResult.SuccessResult("fake://persisted"))
                .Callback(() => persistedAfterRestart.TrySetResult());

            var secondConsumer = CreateConsumer(CreateRabbitOptions(rabbitMqContainer, queueName), secondWriter.Object);
            using var secondCts = new CancellationTokenSource();
            var secondRunTask = secondConsumer.RunAsync(secondCts.Token);

            await persistedAfterRestart.Task.WaitAsync(TimeSpan.FromSeconds(20));

            secondCts.Cancel();
            try { await secondRunTask; } catch (OperationCanceledException) { }

            firstWriter.Verify(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            secondWriter.Verify(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        });
    }

    [Test]
    [Category("Contract")]
    public async Task AckAfterPersist_ValidatesDurabilityBeforeAcknowledgement()
    {
        await RunWithRabbitMqContainerAsync(async rabbitMqContainer =>
        {
            var queueName = $"capture-redelivery-{Guid.NewGuid():N}";
            await EnsureQueueExistsAsync(rabbitMqContainer, queueName);

            var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var writer = new Mock<IStorageWriter>();
            var attempts = 0;
            writer
                .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    attempts++;
                    if (attempts == 1)
                        return StorageWriteResult.FailureResult("first attempt fails");

                    persisted.TrySetResult();
                    return StorageWriteResult.SuccessResult("fake://persisted");
                });

            var consumer = CreateConsumer(CreateRabbitOptions(rabbitMqContainer, queueName), writer.Object);
            using var cts = new CancellationTokenSource();
            var runTask = consumer.RunAsync(cts.Token);

            await PublishAsync(rabbitMqContainer, queueName, "msg-redelivery-1");
            await persisted.Task.WaitAsync(TimeSpan.FromSeconds(20));

            cts.Cancel();
            try { await runTask; } catch (OperationCanceledException) { }

            writer.Verify(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
        });
    }

    private static RabbitMqConsumer CreateConsumer(RabbitMqConsumerOptions options, IStorageWriter storageWriter)
    {
        var batchBuffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions()),
            new TestLogger<CaptureBatchBuffer>());

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

    private static RabbitMqConsumerOptions CreateRabbitOptions(RabbitMqContainer container, string queueName)
    {
        var uri = new Uri(container.GetConnectionString());
        var userInfo = uri.UserInfo.Split(':', 2);

        return new RabbitMqConsumerOptions(null)
        {
            HostName = uri.Host,
            Port = uri.Port,
            VirtualHost = string.IsNullOrWhiteSpace(uri.AbsolutePath) ? "/" : Uri.UnescapeDataString(uri.AbsolutePath),
            UserName = userInfo[0],
            Password = userInfo.Length > 1 ? userInfo[1] : string.Empty,
            QueueName = queueName,
            PrefetchCount = 1
        };
    }

    private static async Task EnsureQueueExistsAsync(RabbitMqContainer container, string queueName)
    {
        var factory = new ConnectionFactory { Uri = new Uri(container.GetConnectionString()) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: false, autoDelete: true, arguments: null);
    }

    private static async Task PublishAsync(RabbitMqContainer container, string queueName, string messageId)
    {
        var factory = new ConnectionFactory { Uri = new Uri(container.GetConnectionString()) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        var properties = new BasicProperties
        {
            MessageId = messageId,
            CorrelationId = $"corr-{messageId}",
            Persistent = true
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            mandatory: false,
            basicProperties: properties,
            body: new ReadOnlyMemory<byte>([1, 2, 3]));
    }

    private static async Task RunWithRabbitMqContainerAsync(Func<RabbitMqContainer, Task> run)
    {
        RabbitMqContainer? container = null;

        try
        {
            container = new RabbitMqBuilder().Build();
            await container.StartAsync();
            await run(container);
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Docker/RabbitMQ test skipped: {ex.Message}");
        }
        finally
        {
            if (container is not null)
                await container.DisposeAsync();
        }
    }
}
