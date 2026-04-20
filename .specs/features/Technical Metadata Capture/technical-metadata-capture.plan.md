# Implementation plan for Technical Metadata Capture

Feature: Technical Metadata Capture

## Tasks

- [x] Confirm architectural placement
  - [x] Identify the ingestion entry point where RabbitMQ messages are consumed
  - [x] Define the capture service boundary while preserving API/application/infrastructure separation

- [x] Define the technical metadata contract
  - [x] Create a dedicated metadata model separate from business payload interpretation
  - [x] Include nullable/optional fields for message identifier, correlation identifier, routing key, exchange, headers, and capture timestamp (UTC)

- [x] Implement RabbitMQ metadata extraction
  - [x] Map transport envelope/properties to the metadata model
  - [x] Handle missing or empty broker fields safely
  - [x] Normalize headers into a storable structure

- [x] Persist raw payload with metadata
  - [x] Extend persistence model/schema to store metadata alongside raw payload
  - [x] Keep metadata as a distinct stored section without business parsing

- [x] Add observability for the capture flow (in scope)
  - [x] Emit structured logs with trace/correlation-friendly fields
  - [x] Propagate and store trace/correlation identifiers when present
  - [x] Add telemetry metrics for capture success and failure paths
  - [x] Keep observability focused on traceability, diagnostics, and replay support for technical metadata capture

- [x] Keep error behavior explicit at ingestion/core scope
  - [x] Capture extraction and persistence failures with consistent structured logs
  - [x] Avoid leaking provider-internal details in persisted artifacts and operational output
  - [x] Do not introduce API/HTTP error mapping in this feature scope

- [x] Apply resilience at infrastructure boundaries
  - [x] Use Polly-based policies for transient broker/persistence failures
  - [x] Do not retry business or validation failures
  - [x] Keep retry behavior observable

## Done criteria

- [x] Captured messages preserve transport metadata independently from business interpretation.
- [x] Stored metadata supports diagnostics, replay scenarios, and traceability.
- [x] Logs and telemetry provide clear visibility into ingestion metadata capture behavior.
- [ ] Implementation builds cleanly.
