# api-http.instructions.md

## Purpose

Define how an API is exposed over HTTP.

---

## Route Design

- paths represent resources or operations
- use path segments for hierarchy
- avoid encoding complex payloads in query strings

---

## HTTP Methods

- GET: read
- POST: create or trigger
- PUT: replace
- PATCH: partial update
- DELETE: remove

---

## Request Binding

- route parameters
- query parameters
- headers
- body

---

## Response Semantics

- consistent response structure
- pagination via query
- content types explicit

---

## Error Mapping

- map exceptions to HTTP status codes
- use consistent error payload
- include traceId

---

## Status Codes

- 200 / 201 / 204
- 400 / 403 / 404 / 409 / 422 / 500

---

## Caching & Concurrency

- ETag support
- conditional requests
- safe concurrency patterns

---

## Observability

- propagate trace headers
- consistent logging boundaries

---

## Security

- authentication headers
- no sensitive data in responses

---

## Anti-Patterns

- misuse of POST
- inconsistent status codes
- leaking stack traces
