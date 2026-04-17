# Design Rules

## Minimal Broker Abstraction

The ingestion layer should use the lowest-level client library (e.g. RabbitMQ.Client) to retain full control over message delivery, acknowledgements, and flow control.
Higher-level frameworks are not allowed unless they demonstrably preserve this control.

### Decision: Use RabbitMQ.Client

#### Rationale:
- full control over ack semantics
- no hidden retries / middleware
- predictable behavior for capture use case

#### Alternatives considered:
- MassTransit → too opinionated for capture
- EasyNetQ → hides delivery semantics

## Configuration Externalization

All RabbitMQ connection details (vHost, queue, credentials) must be defined in external configuration and not hardcoded.

## Single-Queue per Instance

Each capture service instance is bound to exactly one queue to ensure isolation, predictable scaling, and simpler operational behavior.
