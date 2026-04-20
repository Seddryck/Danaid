# Technical Metadata Capture

The ingestion layer captures and stores the technical metadata associated with each consumed message alongside its raw payload.

The capture service

- records transport-level metadata provided by RabbitMQ
- preserves identifiers useful for tracing and correlation
- stores the metadata required to support troubleshooting, replay, and operational analysis
- keeps this metadata separate from any future business interpretation of the message content

This includes, when available

- message identifier
- correlation identifier
- routing key
- exchange
- headers
- capture timestamp

This ensures

- traceability of captured messages
- support for diagnostics and replay scenarios
- preservation of transport context independently from downstream processing
- better observability of ingestion behavior
