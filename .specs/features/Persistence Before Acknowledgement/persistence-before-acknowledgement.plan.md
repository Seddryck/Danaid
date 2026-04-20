# Implementation plan for Persistence Before Acknowledgement

Feature: Persistence Before Acknowledgement

## Tasks

- [x] Define persistence-before-ack contract
  - [x] Document that ack is allowed only after durable write success
  - [x] Document that failed/unpersisted messages are never acked
  - [x] Document broker requeue behavior on persistence failure

- [x] Implement capture flow in `RabbitMqConsumer`
  - [x] Keep manual acknowledgements (`autoAck=false`)
  - [x] Buffer deliveries in `CaptureBatchBuffer` before persistence
  - [x] Persist each batch via `IStorageWriter`
  - [x] Ack each delivery only after successful persistence result

- [x] Implement failure handling and redelivery
  - [x] On persistence failure, issue `basic.nack` with `requeue=true`
  - [x] Remove failed deliveries from local in-flight tracking only after nack
  - [x] Ensure no ack path exists in failure branches

- [x] Preserve at-least-once semantics safely
  - [x] Keep persistence result as the single decision point for ack/nack
  - [x] Keep behavior idempotency-friendly for redelivered messages
  - [x] Avoid hidden retries in consumer logic that conflict with broker redelivery

- [x] Observability and operational signals
  - [x] Log lifecycle and delivery decisions with structured fields (batch ID, delivery tag, message/correlation IDs)
  - [x] Record telemetry for received, failed, retried, and queue lag
  - [x] Track persistence latency and failure counts

- [x] Resilience and architecture compliance
  - [x] Keep resilience policies at infrastructure boundaries (storage adapter)
  - [x] Use Polly for transient technical faults only
  - [x] Keep constructor injection and abstraction boundaries (`IStorageWriter`)

## Done criteria

- [x] No message is acknowledged before durable persistence.
- [x] Persist failures always produce requeue (no premature ack).
- [x] At-least-once semantics are validated by automated tests.
- [x] Logs and telemetry make ack/nack and persistence outcomes auditable.
