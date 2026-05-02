# Error Handling Instructions

## Purpose

Define how the system handles failures end to end so that:
- the API contract is predictable
- exception mapping is centralized
- logs, traces, and metrics are consistent
- retries are explicit and controlled

Error handling is a cross-cutting concern. It must behave the same way across the whole system.

---

## Core Principles

### 1. One failure, one handling flow

A single failure must produce one consistent observable footprint:
- one error code
- one HTTP mapping
- one structured log event
- one trace marked as failed
- one metric increment

Do not let each layer invent its own representation of the same failure.

---

### 2. Centralize handling at the boundary

All exception-to-response mapping must happen in one global mechanism, typically middleware.

Do not:
- map exceptions in controllers
- duplicate response formatting in handlers
- create ad hoc error payloads in endpoints

---

### 3. Distinguish expected from unexpected failures

Expected failures:
- domain exceptions
- not found
- validation failures
- forbidden access
- known conflicts

Unexpected failures:
- bugs
- unclassified provider failures
- null reference issues
- broken assumptions
- unhandled infrastructure failures

Both must be observable, but they must not be treated identically.

---

### 4. Never leak technical internals

External error responses must not expose:
- stack traces
- connection strings
- SQL or provider messages
- internal type names
- file paths

Technical details belong in logs and traces, not in API responses.

---

## End-to-End Error Flow

The required flow is:

`exception -> global middleware -> response mapping + log + trace + metric`

More precisely:

1. an exception is thrown
2. global middleware catches it
3. middleware extracts the stable error code
4. middleware maps it to HTTP status
5. middleware logs one structured event
6. middleware marks the current trace/span as failed
7. middleware increments an error metric using the same code
8. middleware returns a standard error payload including the trace id

This is the reference flow. Do not split these responsibilities across multiple places without a strong reason.

---

## Standard Error Response

All API failures must return the same shape.

```json
{
  "code": "building.already_under_construction",
  "message": "Building is already under construction.",
  "traceId": "00-...-...",
  "details": null
}
```

### Rules

- `code` is mandatory and stable
- `message` is mandatory and human-readable
- `traceId` is mandatory
- `details` is optional and must only contain safe information

Do not expose internal exception names as the public code.

---

## Required Mapping Rules

| Exception Type                  | HTTP Status | Default Code                 |
|--------------------------------|-------------|------------------------------|
| `ValidationException`          | 400         | `request.validation_failed`  |
| `ForbiddenException`           | 403         | `access.forbidden`           |
| `NotFoundException`            | 404         | `resource.not_found`         |
| `ConflictException`            | 409         | `request.conflict`           |
| `UnprocessableEntityException` | 422         | `request.unprocessable`      |
| `DomainException`              | 422         | exception-specific code      |
| unknown exception              | 500         | `system.unexpected_error`    |

If a specific exception type defines its own code, that code wins.

---

## Layer Responsibilities

### Domain Layer

Responsibilities:
- enforce business invariants
- throw domain exceptions

Must not:
- catch and translate to HTTP
- log operational errors
- implement retries

### Application Layer

Responsibilities:
- orchestrate use cases
- propagate domain exceptions
- throw boundary/application exceptions
- translate infrastructure abstractions when useful

Must not:
- build HTTP payloads
- duplicate global logging behavior

### Infrastructure Layer

Responsibilities:
- isolate provider-specific failures
- translate recoverable technical failures when meaningful
- preserve inner exception context for diagnostics

Must not:
- leak raw provider exceptions as part of the API contract
- decide HTTP status codes

### API Layer

Responsibilities:
- centralize error handling
- expose the standard error payload
- include the current trace id
- keep the response safe and stable

---

## Logging Rules

### Single logging point

The final error log must normally be emitted once, in the global middleware.

Lower layers may log only when:
- they add meaningful local operational context
- the log is not a duplicate of the final error log
- the log is about a handled local issue, not the final propagated failure

### Structured fields

Every final error log should contain at least:
- `error.code`
- `error.category`
- `error.type`
- `error.message`
- `trace.id`
- `http.status_code`
- `retryable`
- `path`
- `method`

### Severity model

Recommended default severity:
- validation failures -> Information
- domain violations -> Warning
- forbidden/not found/conflict -> Warning
- unexpected exceptions -> Error

Do not log expected domain failures as fatal technical incidents.

---

## Trace Rules

When an exception reaches global handling:
- mark the active span as failed
- set `error.type`
- set `error.code`
- set `error.message`
- keep `traceId` consistent with the returned payload

Expected business failures can still mark the span as failed if the request failed.
The key is consistency, not optimism.

---

## Metric Rules

A failed request must increment an error metric using the same stable code as the response and logs.

Recommended dimensions:
- `error.code`
- `error.category`
- `http.status_code`
- `endpoint` or route template

Do not use exception class name as the primary metric dimension if the public contract uses stable error codes.

---

## Retry Rules

Retries must be explicit and narrow.

Retry only when all of the following are true:
- the failure is transient
- the operation is safe to retry
- the retry policy is controlled and observable

Never retry:
- `DomainException`
- `ValidationException`
- `ForbiddenException`
- semantic request errors

Do not hide retries from observability. Retry attempts should be visible in telemetry.

---

## Correlation Rules

The response `traceId` must match the tracing/logging context used internally.

If the platform distinguishes between:
- trace id
- correlation id
- causation id

define the relationship explicitly and keep it consistent.
Do not return a random identifier unrelated to operational telemetry.

---

## Implementation Guidance

Preferred implementation:
- one middleware or equivalent global handler
- one response contract type
- one exception-to-status mapping table
- one helper to extract `code`, `category`, `severity`, and `retryable`

Avoid scattering this logic across:
- controllers
- filters
- endpoints
- repositories
- handlers

---

## Anti-Patterns

- catching exceptions and returning custom payloads in controllers
- logging the same propagated exception three times
- translating every exception to 500 without a code
- exposing provider messages to clients
- inventing different error codes in logs and API responses
- retrying business rule violations
- omitting trace id from error responses

---

## Summary

- one failure must produce one consistent observable footprint
- all response mapping must be centralized
- API, logs, traces, and metrics must reuse the same error code
- expected and unexpected failures must be distinguished
- retries must be explicit, safe, and observable
