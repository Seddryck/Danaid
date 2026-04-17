# Design & Technology standards

## Logging

All components must use the Microsoft.Extensions.Logging abstraction for logging.

### Rationale

The ingestion pipeline is a long-running, stateful system where correctness depends on:

- understanding message flow
- tracking batch lifecycle
- diagnosing failures and retries

A consistent logging model is required to ensure:

- uniform structure across components
- integration with hosting environments
- compatibility with multiple logging backends

### Rules

Logging must use ILogger<T> from Microsoft.Extensions.Logging
No component should depend directly on a concrete logging framework (Serilog, NLog, etc.)

Logging configuration (providers, levels, sinks) must be externalized

### Expectations

Logs must provide enough context to:

- trace message ingestion and persistence
- understand batching decisions
- diagnose failures and retries

At minimum, logs should include:

- message identifiers (when available)
- batch identifiers
- timestamps
- error details

## Metrics & Tracing (OpenTelemetry)

The system must expose metrics and traces using OpenTelemetry.

### Rationale

Logging alone is insufficient for operating a streaming ingestion system.
Metrics and traces provide:

- real-time visibility into throughput and latency
- detection of bottlenecks and backpressure
- correlation across distributed components

### Rules

OpenTelemetry must be used as the standard for metrics and tracing
Instrumentation must be integrated at key points of the pipeline
Exporters (Prometheus, OTLP, etc.) must be configurable

### Metrics Expectations

At minimum, the system must expose:

- messages received
- messages persisted
- messages failed
- batch size (count and bytes)
- batch processing latency
- retry counts
- queue lag (if available)

### Tracing Expectations

Traces must allow:

- following a batch through ingestion → persistence → acknowledgement
- identifying latency contributors
- correlating failures across components

Each batch (or logical unit of work) should:

- create a trace span
- include relevant metadata (batch id, size, duration)
