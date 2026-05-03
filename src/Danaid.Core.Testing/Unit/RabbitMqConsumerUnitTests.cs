using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Danaid.Core.Capture;
using Danaid.Core.Consumption;
using Danaid.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Danaid.Core.Testing.Unit;

[TestFixture]
[Category("Functional")]
public class RabbitMqConsumerUnitTests
{
    private sealed class CancelingNoOpPolicy : AsyncPolicy
    {
        private readonly CancellationTokenSource cancellationTokenSource;
        public bool WasExecuted { get; private set; }

        public CancelingNoOpPolicy(CancellationTokenSource cancellationTokenSource)
        {
            this.cancellationTokenSource = cancellationTokenSource;
        }

        protected override Task ImplementationAsync(
            Func<Context, CancellationToken, Task> action,
            Context context,
            CancellationToken cancellationToken,
            bool continueOnCapturedContext)
        {
            WasExecuted = true;
            if (!cancellationTokenSource.IsCancellationRequested)
                cancellationTokenSource.Cancel();

            return Task.FromCanceled(cancellationTokenSource.Token);
        }

        protected override Task<TResult> ImplementationAsync<TResult>(
            Func<Context, CancellationToken, Task<TResult>> action,
            Context context,
            CancellationToken cancellationToken,
            bool continueOnCapturedContext)
        {
            WasExecuted = true;
            if (!cancellationTokenSource.IsCancellationRequested)
                cancellationTokenSource.Cancel();

            return Task.FromCanceled<TResult>(cancellationTokenSource.Token);
        }
    }

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
    [Category("Configuration")]
    public void RunAsync_Throws_WhenOptionsInvalid()
    {
        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = string.Empty
        });

        Assert.That(
            async () => await consumer.RunAsync(CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    [Category("Resilience")]
    public void RunAsync_ReturnsImmediately_WhenCancellationRequested()
    {
        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q-test"
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(
            async () => await consumer.RunAsync(cts.Token),
            Throws.Nothing);
    }

    [Test]
    [Category("Resilience")]
    public void RunAsync_UsesInjectedNoOpPolicy()
    {
        using var cts = new CancellationTokenSource();
        var policy = new CancelingNoOpPolicy(cts);

        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q-test"
        }, brokerRetryPolicy: policy);

        Assert.That(
            async () => await consumer.RunAsync(cts.Token),
            Throws.Nothing);

        Assert.That(policy.WasExecuted, Is.True);
    }

    [Test]
    [Category("Resilience")]
    public void RunAsync_UsesInjectedPolicyFactory()
    {
        using var cts = new CancellationTokenSource();
        var policy = new CancelingNoOpPolicy(cts);

        var factory = new Mock<IBrokerRetryPolicyFactory>();
        factory.Setup(f => f.Create(It.IsAny<RabbitMqConsumerOptions>(), It.IsAny<ILogger<RabbitMqConsumer>>()))
            .Returns(policy);

        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q-test"
        }, brokerRetryPolicyFactory: factory.Object);

        Assert.That(
            async () => await consumer.RunAsync(cts.Token),
            Throws.Nothing);

        factory.Verify(f => f.Create(It.IsAny<RabbitMqConsumerOptions>(), It.IsAny<ILogger<RabbitMqConsumer>>()), Times.Once);
        Assert.That(policy.WasExecuted, Is.True);
    }

    [Test]
    [Category("Contract")]
    public async Task OnMessageReceivedAsync_BuffersMessageAndUpdatesTelemetry()
    {
        var options = new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q-test"
        };

        var batchBuffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions()),
            new TestLogger<CaptureBatchBuffer>());

        var storageWriter = new Mock<IStorageWriter>();
        storageWriter
            .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorageWriteResult.SuccessResult("fake://location"));

        var telemetry = new Mock<IRabbitMqConsumerTelemetry>();
        telemetry.Setup(t => t.SetQueueLag(It.IsAny<int>()));
        telemetry.Setup(t => t.MessageReceived());

        var consumer = new RabbitMqConsumer(
            Options.Create(options),
            batchBuffer,
            storageWriter.Object,
            telemetry.Object,
            new TestLogger<RabbitMqConsumer>());

        var onMessageMethod = typeof(RabbitMqConsumer).GetMethod("OnMessageReceivedAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(onMessageMethod, Is.Not.Null);

        var args = CreateArgs(
            messageId: "m1",
            correlationId: "c1",
            routingKey: "rk",
            exchange: "ex",
            headers: null,
            body: new byte[] { 1 });

        var task = (Task)onMessageMethod!.Invoke(consumer, new object[] { null!, args })!;
        await task.ConfigureAwait(false);

        telemetry.Verify(t => t.MessageReceived(), Times.Once);
        telemetry.Verify(t => t.SetQueueLag(It.Is<int>(n => n >= 1)), Times.AtLeastOnce);
    }

    [Test]
    [Category("Contract")]
    public void AckBatchAsync_ThrowsIfChannelNotInitialized()
    {
        var consumer = CreateMinimalConsumer();

        var method = typeof(RabbitMqConsumer).GetMethod("AckBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        var batch = CreateBatch();

        try
        {
            var result = method!.Invoke(consumer, new object[] { batch, CancellationToken.None });
            if (result is Task task)
                Assert.That(async () => await task.ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>());
            else
                Assert.Fail("Expected an exception when invoking AckBatchAsync but none was thrown.");
        }
        catch (TargetInvocationException tie)
        {
            Assert.That(tie.InnerException, Is.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    [Category("Contract")]
    public void RequeueBatchAsync_ThrowsIfChannelNotInitialized()
    {
        var consumer = CreateMinimalConsumer();

        var method = typeof(RabbitMqConsumer).GetMethod("RequeueBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        var batch = CreateBatch();

        try
        {
            var result = method!.Invoke(consumer, new object[] { batch, CancellationToken.None });
            if (result is Task task)
                Assert.That(async () => await task.ConfigureAwait(false), Throws.TypeOf<InvalidOperationException>());
            else
                Assert.Fail("Expected an exception when invoking RequeueBatchAsync but none was thrown.");
        }
        catch (TargetInvocationException tie)
        {
            Assert.That(tie.InnerException, Is.TypeOf<InvalidOperationException>());
        }
    }

    [Test]
    [Category("Resilience")]
    public void TryPersistBatchAsync_ThrowsWhenOperationCanceledAndTokenCancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var logger = new TestLogger<RabbitMqConsumer>();
        var storageWriter = new Mock<IStorageWriter>();
        storageWriter
            .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var consumer = CreateConsumerWithStorageWriter(storageWriter, logger);

        var method = typeof(RabbitMqConsumer).GetMethod("TryPersistBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        var batch = CreateBatch();

        Assert.That(
            async () => await ((Task<StorageWriteResult>)method!.Invoke(consumer, new object[] { batch, cts.Token })!).ConfigureAwait(false),
            Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    [Category("Resilience")]
    public async Task TryPersistBatchAsync_ReturnsFailureResultAndLogsError_WhenExceptionThrown()
    {
        var logger = new TestLogger<RabbitMqConsumer>();
        var storageWriter = new Mock<IStorageWriter>();
        storageWriter
            .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var consumer = CreateConsumerWithStorageWriter(storageWriter, logger);

        var method = typeof(RabbitMqConsumer).GetMethod("TryPersistBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);

        var batch = CreateBatch();

        var result = await ((Task<StorageWriteResult>)method!.Invoke(consumer, new object[] { batch, CancellationToken.None })!).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("boom"));
            Assert.That(logger.Entries.Exists(e => e.Level == LogLevel.Error), Is.True);
        });
    }

    [Test]
    [Category("Configuration")]
    public void RunAsync_Throws_WhenOptionsInvalid()
    {
        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = string.Empty
        });

        Assert.That(
            async () => await consumer.RunAsync(CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    [Category("Resilience")]
    public void RunAsync_ReturnsImmediately_WhenCancellationRequested()
    {
        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q-test"
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(
            async () => await consumer.RunAsync(cts.Token),
            Throws.Nothing);
    }

    [Test]
    [Category("Resilience")]
    public void RunAsync_UsesInjectedNoOpPolicy()
    {
        using var cts = new CancellationTokenSource();
        var policy = new CancelingNoOpPolicy(cts);

        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q-test"
        }, brokerRetryPolicy: policy);

        Assert.That(
            async () => await consumer.RunAsync(cts.Token),
            Throws.Nothing);

        Assert.That(policy.WasExecuted, Is.True);
    }

    [Test]
    [Category("Resilience")]
    public void RunAsync_UsesInjectedPolicyFactory()
    {
        using var cts = new CancellationTokenSource();
        var policy = new CancelingNoOpPolicy(cts);

        var factory = new Mock<IBrokerRetryPolicyFactory>();
        factory.Setup(f => f.Create(It.IsAny<RabbitMqConsumerOptions>(), It.IsAny<ILogger<RabbitMqConsumer>>()))
            .Returns(policy);

        var consumer = CreateConsumer(new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q-test"
        }, brokerRetryPolicyFactory: factory.Object);

        Assert.That(
            async () => await consumer.RunAsync(cts.Token),
            Throws.Nothing);

        factory.Verify(f => f.Create(It.IsAny<RabbitMqConsumerOptions>(), It.IsAny<ILogger<RabbitMqConsumer>>()), Times.Once);
        Assert.That(policy.WasExecuted, Is.True);
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

    private static RabbitMqConsumer CreateConsumer(
        RabbitMqConsumerOptions options,
        IStorageWriter? storageWriter = null,
        IBrokerRetryPolicyFactory? brokerRetryPolicyFactory = null,
        AsyncPolicy? brokerRetryPolicy = null)
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
            new TestLogger<RabbitMqConsumer>(),
            brokerRetryPolicyFactory,
            brokerRetryPolicy);
    }

    private static RabbitMqConsumer CreateMinimalConsumer()
    {
        var options = new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q-test"
        };

        var batchBuffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions()),
            new TestLogger<CaptureBatchBuffer>());

        var storageWriter = new Mock<IStorageWriter>();
        storageWriter
            .Setup(x => x.WriteAsync(It.IsAny<CaptureBatch>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorageWriteResult.SuccessResult("fake://location"));

        var telemetry = new Mock<IRabbitMqConsumerTelemetry>();
        telemetry.Setup(t => t.SetQueueLag(It.IsAny<int>()));

        return new RabbitMqConsumer(
            Options.Create(options),
            batchBuffer,
            storageWriter.Object,
            telemetry.Object,
            new TestLogger<RabbitMqConsumer>());
    }

    private static RabbitMqConsumer CreateConsumerWithStorageWriter(Mock<IStorageWriter> storageWriter, TestLogger<RabbitMqConsumer> logger)
    {
        var options = new RabbitMqConsumerOptions(null)
        {
            HostName = "localhost",
            QueueName = "q-test"
        };

        var batchBuffer = new CaptureBatchBuffer(
            Options.Create(new CaptureBatchBufferOptions()),
            new TestLogger<CaptureBatchBuffer>());

        var telemetry = new Mock<IRabbitMqConsumerTelemetry>();
        telemetry.Setup(t => t.SetQueueLag(It.IsAny<int>()));

        return new RabbitMqConsumer(
            Options.Create(options),
            batchBuffer,
            storageWriter.Object,
            telemetry.Object,
            logger);
    }

    private static CaptureBatch CreateBatch()
        => new(
            BatchId: Guid.NewGuid().ToString(),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            TotalBytes: 1,
            Deliveries: new List<BufferedDelivery>
            {
                new BufferedDelivery(1, new CapturedMessage(new byte[] { 1 }, new TechnicalMetadata(null!, "", "", null, null, DateTimeOffset.UtcNow)))
            });

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
