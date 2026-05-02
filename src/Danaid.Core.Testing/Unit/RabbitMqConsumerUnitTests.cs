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
}
