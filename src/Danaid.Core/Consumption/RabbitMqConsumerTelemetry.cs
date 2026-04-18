using System.Diagnostics.Metrics;
using System.Threading;

namespace Danaid.Core.Consumption;

public interface IRabbitMqConsumerTelemetry
{
    void MessageReceived();
    void BatchFailed(int messageCount);
    void BatchRetried(int messageCount);
    void SetQueueLag(int inFlightCount);
}

public sealed class RabbitMqConsumerTelemetry : IRabbitMqConsumerTelemetry
{
    private static readonly Meter Meter = new("Danaid.Ingestion.RabbitMqConsumer", "1.0.0");

    private readonly Counter<long> receivedCounter = Meter.CreateCounter<long>("danaid.messages.received");
    private readonly Counter<long> failedCounter = Meter.CreateCounter<long>("danaid.messages.failed");
    private readonly Counter<long> retryCounter = Meter.CreateCounter<long>("danaid.messages.retry");
    private long queueLag;

    public RabbitMqConsumerTelemetry()
    {
        Meter.CreateObservableGauge("danaid.queue.lag", () => Interlocked.Read(ref queueLag));
    }

    public void MessageReceived() => receivedCounter.Add(1);

    public void BatchFailed(int messageCount) => failedCounter.Add(messageCount);

    public void BatchRetried(int messageCount) => retryCounter.Add(messageCount);

    public void SetQueueLag(int inFlightCount) => Interlocked.Exchange(ref queueLag, inFlightCount);
}
