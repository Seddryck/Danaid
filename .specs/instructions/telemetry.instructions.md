# Telemetry Guidelines

## Purpose

Define how telemetry must represent failures so that:
- logs, traces, and metrics tell the same story
- errors are searchable by stable identifiers
- API responses can be correlated with operational data
- retry behavior and failure severity are visible

Telemetry is not decoration. It is part of the operational contract of the system.

---

## Core Principles

### 1. Use one shared error identity

Every failure must be identified by the same stable error code across:
- API responses
- logs
- traces
- metrics

The error code comes from the exception model.
Do not invent separate identifiers for each telemetry channel.

---

### 2. Prefer structured telemetry over free text

Operators must be able to search and aggregate by fields, not by string fragments.

Always prefer:
- `error.code=building.already_under_construction`

Over:
- `"the building seems to already be under construction"`

Human-readable messages are useful, but they do not replace structured fields.

---

### 3. Correlate everything

Every error must be correlatable through:
- `trace.id`
- request path
- route or endpoint name
- operation name

The `traceId` returned to the client must be the same one visible in telemetry.

---

### 4. Separate expected business failures from unexpected technical failures

Both matter.
They must not disappear from telemetry.

However, expected business failures should not look identical to platform incidents.

---

## Required Failure Fields

Final failure logs and trace attributes should expose at least:

- `error.code`
- `error.category`
- `error.type`
- `error.message`
- `error.severity`
- `retryable`
- `trace.id`
- `http.status_code`
- `http.request.method`
- `url.path`
- `operation.name`

If the platform supports route templates, also include:
- `http.route`

---

## Error Categories

Use categories consistent with the exception model:
- `domain`
- `application`
- `system`

Do not derive categories ad hoc in telemetry if the exception model already defines them.

---

## Logging Guidance

### Final error log

The global error handler should emit the final authoritative error log.

Example structured log intent:

- level: Warning
- message: request failed because building is already under construction
- fields:
  - `error.code=building.already_under_construction`
  - `error.category=domain`
  - `error.type=BuildingAlreadyUnderConstructionException`
  - `http.status_code=422`
  - `trace.id=...`
  - `retryable=false`

### Log level guidance

Recommended defaults:
- validation failures -> Information
- expected access/not found/conflict/domain failures -> Warning
- unexpected system failures -> Error

Do not generate Error logs for every business rule violation unless there is a specific operational reason.

### Duplicates

Avoid duplicate logging across layers.
If a propagated exception is already logged as the final request failure, do not log it again upstream unless extra local context is necessary and clearly different.

---

## Tracing Guidance

### Span failure semantics

When a request fails:
- mark the active span as failed
- add `error.code`
- add `error.type`
- add `error.message`

Expected failures can still mark the span as failed because the request outcome is a failure from the caller's perspective.

### Retry visibility

If retries occur:
- keep them visible as events or child spans
- include attempt count
- include failure cause per attempt when useful

Do not compress retries into silence.

---

## Metric Guidance

### Required metric

Track failed requests with dimensions based on stable error identity.

Recommended metric:
- `requests_failed_total`

Recommended dimensions:
- `error.code`
- `error.category`
- `http.status_code`
- `http.route`
- `operation.name`

### Why code matters

Aggregate by `error.code`, not only by exception type.
Exception class names can change internally.
The code is the durable contract.

### Optional supporting metrics

Consider:
- `retries_total`
- `unexpected_errors_total`
- `validation_failures_total`

Even when specialized metrics exist, the canonical error dimension remains the same error code.

---

## OpenTelemetry Alignment

When using OpenTelemetry:
- put stable failure attributes on spans
- ensure logs include the trace context
- ensure metrics and logs can be joined operationally through shared dimensions and route names

Suggested span attributes:
- `error.code`
- `error.type`
- `error.category`
- `retryable`
- `http.response.status_code`

Suggested log fields:
- `trace_id`
- `span_id`
- `error.code`
- `error.type`
- `error.category`

Suggested metric dimensions:
- `error.code`
- `error.category`
- `http.status_code`
- `http.route`

Naming can follow platform conventions, but semantics must stay aligned.

---

## Correlation Rules

The client-visible `traceId` must correspond to the same request trace used in:
- logs
- spans
- final error response

If a separate correlation id is also used, document the rule clearly.
Do not let correlation become ambiguous.

---

## Redaction and Safety

Telemetry may contain more detail than API responses, but it still must be safe.

Do not put secrets, credentials, personal data, or raw secure payloads into:
- logs
- span attributes
- metric tags

If error details contain sensitive data, sanitize before recording.

---

## Implementation Guidance

A minimal aligned model usually needs:
- a stable error code on exceptions
- one global error handler
- one helper that enriches logs, traces, and metrics from the same exception metadata
- one standard API error payload containing the trace id

This alignment matters more than the exact library choice.

---

## Anti-Patterns

- using exception class names as the only identifier
- different error codes in logs and API payloads
- returning a trace id that cannot be found in telemetry
- logging stack traces for expected validation failures by default
- tagging metrics with high-cardinality raw messages
- hiding retries from traces and metrics

---

## Summary

- telemetry must reuse the same stable error code as the API contract
- logs, traces, and metrics must describe the same failure consistently
- the returned trace id must correlate with operational telemetry
- expected failures remain visible but should not look like random system incidents
