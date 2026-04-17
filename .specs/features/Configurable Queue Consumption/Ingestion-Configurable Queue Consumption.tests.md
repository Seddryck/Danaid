# Test strategy for Configurable Queue Consumption

Feature: Configurable Queue Consumption
Integration tests
- [x] reconnect → validates persistent consumer lifecycle and recovery (implemented as environment-dependent ignored test)
- [x] backpressure → validates prefetch + bounded in-flight behavior
- [x] ack-after-persist (rename from back-after-persist) → validates durability-before-ack (implemented as environment-dependent ignored test)
- [x] duplicate handling → validates idempotency support via stable identifiers
- [x] single-queue-per-instance → validates runtime topology contract
- [x] configuration externalization → validates queue/vHost/credentials are config-driven
- [x] metadata persistence → validates headers/routing key/correlation/message IDs are preserved

Observability tests
- [x] verify required logs → validates ILogger<T> usage and required fields (message ID, batch ID, timestamp, error)
- [x] metrics are emitted → validates required OTel metrics (received/persisted/failed, batch size/latency, retries, lag if available)
- [x] trace spans are emitted → validates ingestion → persistence → acknowledgement trace path with batch metadata
