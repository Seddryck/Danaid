# RabbitMQ Message Consumption

The system establishes a persistent connection to RabbitMQ and consumes messages using a push-based model with manual acknowledgement.

Consumption is designed to be:

- reliable, with explicit control over acknowledgements
- continuous, supporting long-running ingestion processes
- backpressure-aware, using prefetch limits to avoid overload

Messages are received together with their full metadata (headers, routing key, correlation identifiers), ensuring no loss of contextual information.
