using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Danaid.Core.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Danaid.Core;

public sealed class RabbitMqConsumer : IAsyncDisposable
{
    private readonly RabbitMqConsumerOptions options;
    private readonly CaptureBatchBuffer batchBuffer;
    private readonly IStorageWriter storageWriter;
    private readonly IRabbitMqConsumerTelemetry telemetry;
    private readonly ILogger<RabbitMqConsumer> logger;

    private readonly ConcurrentDictionary<ulong, BufferedDelivery> inFlight = new();

    private IConnection? connection;
    private IChannel? channel;
    private string? consumerTag;

    public RabbitMqConsumer(
        IOptions<RabbitMqConsumerOptions> options,
        CaptureBatchBuffer batchBuffer,
        IStorageWriter storageWriter,
        IRabbitMqConsumerTelemetry telemetry,
        ILogger<RabbitMqConsumer> logger)
    {
        this.options = options.Value;
        this.batchBuffer = batchBuffer;
        this.storageWriter = storageWriter;
        this.telemetry = telemetry;
        this.logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(cancellationToken);
                await ProcessBatchesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RabbitMQ consumer loop failure. Reconnecting in {Delay}.", options.ReconnectDelay);
                await Task.Delay(options.ReconnectDelay, cancellationToken);
            }
            finally
            {
                await DisposeConnectionAsync();
            }
        }
    }

    private async Task ConnectAndConsumeAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.HostName,
            Port = options.Port,
            VirtualHost = options.VirtualHost,
            UserName = options.UserName,
            Password = options.Password
        };

        connection = await factory.CreateConnectionAsync(cancellationToken: cancellationToken);
        channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: options.PrefetchCount, global: false, cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += OnMessageReceivedAsync;

        consumerTag = await channel.BasicConsumeAsync(
            queue: options.QueueName,
            autoAck: false,
            consumerTag: options.ConsumerTag ?? string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation("RabbitMQ consumer started. Queue={QueueName} ConsumerTag={ConsumerTag}", options.QueueName, consumerTag);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        var headers = args.BasicProperties.Headers is null
            ? null
            : new Dictionary<string, object?>(args.BasicProperties.Headers);

        var message = new CapturedMessage(
            Body: args.Body.ToArray(),
            Headers: headers,
            RoutingKey: args.RoutingKey,
            Exchange: args.Exchange,
            CorrelationId: args.BasicProperties.CorrelationId,
            MessageId: args.BasicProperties.MessageId,
            TimestampUtc: DateTimeOffset.UtcNow);

        var buffered = new BufferedDelivery(args.DeliveryTag, message);
        inFlight[args.DeliveryTag] = buffered;
        telemetry.SetQueueLag(inFlight.Count);

        await batchBuffer.EnqueueAsync(buffered, CancellationToken.None);
        telemetry.MessageReceived();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Message received. DeliveryTag={DeliveryTag} MessageId={MessageId} CorrelationId={CorrelationId}",
            args.DeliveryTag,
            message.MessageId,
            message.CorrelationId);
        }
    }

    private async Task ProcessBatchesAsync(CancellationToken cancellationToken)
    {
        if (channel is null)
            throw new InvalidOperationException("RabbitMQ channel not initialized.");

        await foreach (var batch in batchBuffer.ReadBatchesAsync(cancellationToken))
        {
            var result = await storageWriter.WriteAsync(batch, cancellationToken);

            if (result.Success)
            {
                foreach (var delivery in batch.Deliveries)
                {
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
                    inFlight.TryRemove(delivery.DeliveryTag, out _);
                    telemetry.SetQueueLag(inFlight.Count);
                }

                continue;
            }

            telemetry.BatchFailed(batch.Deliveries.Count);
            telemetry.BatchRetried(batch.Deliveries.Count);

            foreach (var delivery in batch.Deliveries)
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken: cancellationToken);
                inFlight.TryRemove(delivery.DeliveryTag, out _);
                telemetry.SetQueueLag(inFlight.Count);
            }

            logger.LogWarning("Batch persistence failed. BatchId={BatchId}. Messages requeued.", batch.BatchId);
        }
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(options.QueueName))
            throw new InvalidOperationException("QueueName is required.");

        if (options.QueueName.Contains(',') || options.QueueName.Contains(';'))
            throw new InvalidOperationException("Single queue per instance is required.");

        if (string.IsNullOrWhiteSpace(options.HostName))
            throw new InvalidOperationException("HostName is required.");
    }

    private async ValueTask DisposeConnectionAsync()
    {
        try
        {
            if (channel is not null && !string.IsNullOrWhiteSpace(consumerTag))
                await channel.BasicCancelAsync(consumerTag, false, CancellationToken.None);
        }
        catch
        {
        }

        if (channel is not null)
            await channel.CloseAsync(CancellationToken.None);

        if (connection is not null)
            await connection.CloseAsync(CancellationToken.None);

        channel = null;
        connection = null;
        consumerTag = null;
        telemetry.SetQueueLag(0);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeConnectionAsync();
    }
}
