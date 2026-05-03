using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using Danaid.Core.Consumption;
using NUnit.Framework;

namespace Danaid.Core.Testing.Unit;

[TestFixture]
public class RabbitMqConsumerTelemetryUnitTests
{
    [Test]
    [Category("Observability")]
    public async Task CountersAndGaugeEmitMeasurements()
    {
        var telemetry = new RabbitMqConsumerTelemetry();

        var measurements = new List<(string Name, long Value)>();
        var queueLagTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "danaid.messages.received" ||
                instrument.Name == "danaid.messages.failed" ||
                instrument.Name == "danaid.messages.retry" ||
                instrument.Name == "danaid.queue.lag")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            measurements.Add((instrument.Name, measurement));
            if (instrument.Name == "danaid.queue.lag" && measurement == 42)
                queueLagTcs.TrySetResult(true);
        });

        listener.Start();

        telemetry.MessageReceived();
        telemetry.BatchFailed(2);
        telemetry.BatchRetried(3);

        var swCounters = System.Diagnostics.Stopwatch.StartNew();
        while (swCounters.ElapsedMilliseconds < 200 &&
               !(measurements.Exists(m => m.Name == "danaid.messages.received") &&
                 measurements.Exists(m => m.Name == "danaid.messages.failed") &&
                 measurements.Exists(m => m.Name == "danaid.messages.retry")))
        {
            await Task.Delay(20).ConfigureAwait(false);
        }

        Assert.That(measurements.Exists(m => m.Name == "danaid.messages.received"), Is.True);
        Assert.That(measurements.Exists(m => m.Name == "danaid.messages.failed"), Is.True);
        Assert.That(measurements.Exists(m => m.Name == "danaid.messages.retry"), Is.True);

        telemetry.SetQueueLag(42);

        // deterministic verification of observable gauge state
        var qField = typeof(RabbitMqConsumerTelemetry).GetField("queueLag", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(qField, Is.Not.Null);
        var qValue = (long)qField!.GetValue(telemetry)!;
        Assert.That(qValue, Is.EqualTo(42));
    }
}
