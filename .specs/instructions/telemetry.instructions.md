---
applyTo: "**/*.cs"
---

# Telemetry Guidance

Telemetry must support the code, not shape it.

## Principle

Application and domain classes must not define telemetry infrastructure themselves.
Their responsibility is to execute business or technical behavior. Telemetry is a supporting concern and must stay secondary in the code structure.

## Rules

### 1. Do not define meters directly inside application classes

Avoid creating `Meter`, `Counter`, `Histogram`, `ObservableGauge`, or similar telemetry primitives directly inside service, adapter, app service, handler, repository, or consumer classes.

Do not write code like:

```csharp
private readonly Meter meter = new("MyApp.Component", "1.0.0");
private readonly Counter<long> counter;
```

Telemetry primitives must be defined in a dedicated telemetry or instrumentation class.

2. Keep telemetry setup outside the operational flow

Constructors of operational classes must not spend visible space configuring telemetry.
A constructor should mainly show business dependencies and operational collaborators.

If a class needs telemetry, inject a dedicated telemetry collaborator instead of building counters and gauges itself.

Preferred:

```csharp
public MyConsumer(IMyConsumerTelemetry telemetry, ...)
```

Avoid:

```csharp
public MyConsumer(...)
{
    counter = meter.CreateCounter<long>("...");
    gauge = meter.CreateObservableGauge("...", ...);
}
```

3. Hide telemetry behind a dedicated abstraction

When a component needs to emit telemetry, it should call intention-revealing methods such as:

```csharp
telemetry.MessageReceived();
telemetry.BatchFailed(count);
telemetry.BatchRetried(count);
```

Avoid exposing raw telemetry concepts in the operational code such as repeated .Add(...), histogram recording, tag construction, or observable registration.

The calling code should read as application logic, not as instrumentation logic.

4. Observable telemetry must not expose internal state management in the main class

If gauges or observable instruments need access to internal state, that logic must be encapsulated in a dedicated telemetry component or instrumentation holder.

Operational classes should not add methods whose main purpose is to feed telemetry, such as:

```csharp
private IEnumerable<Measurement<long>> ObserveQueueLag()
```

If such observation is needed, move it to a telemetry-specific type.

5. Telemetry calls must remain sparse and intention-focused

Telemetry is acceptable inside the flow when it is limited to small, clear calls reflecting meaningful events.

Good:

```csharp
telemetry.MessageReceived();
telemetry.BatchPersisted(batch.Count);
telemetry.BatchRejected(batch.Count);
```

Bad:

```csharp
receivedCounter.Add(1);
processingCounter.Add(1, tagA, tagB);
latencyHistogram.Record(value, tagA, tagB);
retryCounter.Add(count);
```

The first form preserves readability. The second makes telemetry dominate the code.

6. Do not duplicate business concepts only for telemetry

Do not introduce methods, variables, or state whose only purpose is to make metrics easier if that makes the class model less clear.

Telemetry must follow the design of the component, not drive it.

7. Favor dedicated instrumentation classes per component family

When needed, create dedicated types such as:

- RabbitMqConsumerTelemetry
- BlobStorageWriterTelemetry
- IngestionPipelineTelemetry

These types own:

- meter creation
- instrument creation
- metric naming
- tag conventions
- observable registration

Operational classes only use them.

8. Metric names and tags belong to the telemetry layer

Names such as danaid.messages.received or tags such as queue name, outcome, or storage type must be centralized in the telemetry component.

Do not spread metric names and telemetry tag conventions across business or adapter classes.

9. Logging and telemetry are both observability, but they should stay lightweight in the core flow

Logging statements are allowed in operational classes because they express runtime events directly.
However, telemetry must remain even less visible than logging.

If telemetry is visually competing with the main flow, the design is wrong.

## Preferred pattern

Use a dedicated interface and implementation for each important instrumented component.

Example:

```csharp
public interface IRabbitMqConsumerTelemetry
{
    void MessageReceived();
    void BatchFailed(int messageCount);
    void BatchRetried(int messageCount);
}
public sealed class RabbitMqConsumerTelemetry : IRabbitMqConsumerTelemetry
{
    private static readonly Meter Meter = new("Danaid.Ingestion.RabbitMqConsumer", "1.0.0");
    private readonly Counter<long> receivedCounter = Meter.CreateCounter<long>("danaid.messages.received");
    private readonly Counter<long> failedCounter = Meter.CreateCounter<long>("danaid.messages.failed");
    private readonly Counter<long> retryCounter = Meter.CreateCounter<long>("danaid.messages.retry");

    public void MessageReceived() => receivedCounter.Add(1);
    public void BatchFailed(int messageCount) => failedCounter.Add(messageCount);
    public void BatchRetried(int messageCount) => retryCounter.Add(messageCount);
}
```

Then the consumer stays focused on its real behavior:

```csharp
telemetry.MessageReceived();
telemetry.BatchFailed(batch.Deliveries.Count);
telemetry.BatchRetried(batch.Deliveries.Count);
```

## Review checklist

When reviewing code, reject or refactor when one or more of the following is true:

- the class creates its own Meter
- the constructor contains visible telemetry setup
- the main flow is interleaved with raw metric operations
- the class exposes telemetry-specific observation methods
- metric names or tags are hardcoded in operational classes
- telemetry is more visible than the business or technical behavior

## Desired outcome

A reader should be able to understand the responsibility and flow of a class without being distracted by instrumentation details.

Telemetry must be present, but discreet.
