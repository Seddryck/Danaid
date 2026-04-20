# Implementaion plan for Configurable Queue Consumption

Feature: Configurable Queue Consumption
Tasks
- [x] Implement RabbitMqConsumer
    - [x] Use RabbitMQ.Client with persistent connection and automatic reconnect
    - [x] Enforce single-queue-per-instance from external configuration
    - [x] Configure push-based consumption with manual acknowledgement
    - [x] Apply prefetch (basic.qos) for bounded in-flight messages
    - [x] Capture full message metadata (headers, routing key, correlation/message IDs)
    - [x] Acknowledge only after durable persistence confirmation
    - [x] Use ILogger<RabbitMqConsumer> for lifecycle and delivery decision logs
    - [x] Add OpenTelemetry metrics: received, failed, retry, queue lag (if available)
- [x] Implement CaptureBatchBuffer
    - [x] Accept captured messages without business filtering/transformation
    - [x] Buffer with bounded capacity and explicit backpressure behavior
    - [x] Support batch policies from config (count/size/time)
    - [x] Track batch identifiers and message counts/bytes
    - [x] Emit ILogger<CaptureBatchBuffer> logs for batch open/flush/retry
    - [x] Emit OpenTelemetry metrics: batch size (count/bytes), batch latency
    - [x] Create trace span per batch with batch metadata
- [x] Implement StorageWriter
    - [x] Persist batches durably before returning success
    - [x] Return explicit outcome to drive ack/nack behavior
    - [x] Preserve identifiers required for downstream idempotency
    - [x] Implement retry policy via externalized settings
    - [x] Emit ILogger<FileSystemStorageWriter> logs with batch ID, timestamps, errors
    - [x] Emit OpenTelemetry metrics: persisted, failed, retry count
    - [x] Emit trace spans covering persistence and acknowledgement handoff
- [x] Extract storage abstraction for pluggable backends
    - [x] Introduce IStorageWriter as ingestion storage contract
    - [x] Rename current implementation to FileSystemStorageWriter
    - [x] Make RabbitMqConsumer depend on IStorageWriter only
    - [x] Keep file/folder path behavior in FileSystemStorageWriter options
    - [x] Prepare extension point for BlobStorageWriter (future adapter)
    - [x] Update tests to target contract behavior + filesystem adapter behavior separately
