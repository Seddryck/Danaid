# Persistence Before Acknowledgement

The ingestion layer acknowledges messages only after they have been durably persisted.

The capture service:

* buffers incoming messages until they are written to storage
* persists each message before acknowledging it to RabbitMQ
* does not acknowledge messages that have not been successfully persisted
* requeues messages when persistence fails

This ensures:

* protection against message loss caused by premature acknowledgement
* at-least-once delivery semantics from the ingestion perspective
* ability to recover from transient storage or processing failures
* clear separation between message capture and downstream processing
