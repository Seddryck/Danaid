# API Design Instructions

## Purpose

Define the API as an architectural boundary independent of transport.

The API:
- exposes use cases and information
- defines a stable contract for consumers
- isolates domain and application from external concerns

---

## Core Principles

### API is a contract
- stable and explicit
- independent from implementation
- designed for consumers, not internal structure

### Separation of concerns
- API: contract and boundary
- Application: orchestration
- Domain: business rules
- Infrastructure: technical implementation

### No transport leakage
- no HTTP concepts
- no protocol-specific semantics
- no framework-specific constraints

---

## Operation Modeling

### Commands vs Queries
- commands: change state
- queries: retrieve information

### Use-case orientation
- operations represent intent
- not CRUD by default

### Idempotency (semantic)
- defined at use-case level
- independent from transport

---

## Contract Design

- DTOs separate from domain models
- strong typing (IDs, enums)
- explicit fields
- backward compatibility required

---

## Validation

- structural validation at API boundary
- semantic validation in application
- business rules in domain

---

## Authorization

- API controls access to boundary
- application enforces use-case authorization
- domain does not handle caller identity

---

## Error Contract

- API exposes stable error structure
- aligned with system exception model
- no internal leakage

---

## Observability

- API propagates correlation context
- API ensures traceability of operations

---

## Versioning

- explicit versioning strategy
- backward compatibility first
- deprecation must be controlled

---

## Anti-Patterns

- exposing domain objects directly
- mixing transport concerns
- letting infrastructure define API
