# Exception Model Specification

## Purpose

Define the exception model of the system so that:
- business rule violations are explicit
- interaction failures are predictable
- API responses are consistent
- logs, traces, and metrics use the same identifiers

Exceptions are part of the contract of the system. They are not just implementation details.

---

## Core Principles

### 1. Exceptions express intent

Every thrown exception must clearly communicate:
- what failed
- why it failed
- whether it is a business failure or an interaction failure

Avoid vague exceptions such as:
- `Exception`
- `InvalidOperationException`
- `ApplicationException`

Use explicit domain or application exception types instead.

---

### 2. Domain exceptions protect invariants

Domain exceptions represent violations of business rules and invariants.

They:
- live in the domain layer
- do not depend on HTTP, persistence, messaging, or framework concepts
- are deterministic and meaningful in business language

They must not:
- expose technical details
- contain transport concerns
- be used for input formatting issues

Examples:
- building already under construction
- negative or zero duration not allowed
- invalid state transition
- resource quantity cannot become negative

---

### 3. Application exceptions protect interaction boundaries

Application exceptions represent failures related to:
- missing resources
- forbidden access
- state conflicts
- invalid input
- semantic invalidity of a request

They sit at the boundary between:
- domain
- infrastructure
- API

---

### 4. Exceptions must be observable

Every exception type must expose a stable error code.

That code is reused in:
- API responses
- structured logs
- metrics dimensions
- trace tags

The error code is the stable identifier of the failure contract.

---

## Required Base Model

Each system exception must expose, directly or through inheritance:

- `Code`: stable machine-readable identifier
- `Message`: human-readable message
- `Category`: domain or application
- `IsTransient`: whether retry may make sense
- `LogLevel`: expected severity for observability

Example:

```csharp
public abstract class SystemExceptionBase : Exception
{
    protected SystemExceptionBase(string message) : base(message) { }

    public abstract string Code { get; }
    public abstract string Category { get; }
    public virtual bool IsTransient => false;
    public virtual LogLevel LogLevel => LogLevel.Warning;
}
```

If `LogLevel` should not leak Microsoft logging abstractions into the domain, define your own severity enum instead.

Example:

```csharp
public enum ErrorSeverity
{
    Info,
    Warning,
    Error
}
```

---

## Domain Exception Base Type

Domain exceptions should inherit from a dedicated base type.

```csharp
public abstract class DomainException : Exception
{
    protected DomainException(string message) : base(message) { }

    public abstract string Code { get; }
    public virtual string Category => "domain";
    public virtual bool IsTransient => false;
    public virtual ErrorSeverity Severity => ErrorSeverity.Warning;
}
```

Example:

```csharp
public sealed class BuildingAlreadyUnderConstructionException : DomainException
{
    public BuildingAlreadyUnderConstructionException(BuildingId id)
        : base($"Building '{id}' is already under construction.")
    {
    }

    public override string Code => "building.already_under_construction";
}
```

---

## Application Exception Base Type

```csharp
public abstract class AppException : Exception
{
    protected AppException(string message) : base(message) { }

    public abstract string Code { get; }
    public virtual string Category => "application";
    public virtual bool IsTransient => false;
    public virtual ErrorSeverity Severity => ErrorSeverity.Warning;
}
```

Common application exceptions:
- `NotFoundException`
- `ForbiddenException`
- `ConflictException`
- `ValidationException`
- `UnprocessableEntityException`

Examples of codes:
- `resource.not_found`
- `access.forbidden`
- `request.conflict`
- `request.validation_failed`
- `request.unprocessable`

---

## Domain vs Validation

Keep a strict distinction.

### DomainException
Use when a business rule is violated.

Examples:
- a building cannot start construction twice
- a stock cannot become negative
- a duration in the business model cannot be zero

### ValidationException
Use when input is structurally invalid.

Examples:
- missing required field
- malformed identifier
- invalid JSON payload shape
- unsupported enum literal in request payload

### UnprocessableEntityException
Use when the request is structurally valid but semantically invalid from an application perspective.

Examples:
- requested action cannot be executed in the current workflow state
- filter combination is unsupported by the application contract

Do not use `ValidationException` as a replacement for domain modeling.

---

## Where Exceptions Are Thrown

### Domain Layer
Allowed:
- `DomainException`

Not allowed:
- HTTP exceptions
- infrastructure exceptions
- transport exceptions
- controller-specific exceptions

### Application Layer
Allowed:
- application exceptions
- propagation of domain exceptions
- translation of domain exceptions when the public contract requires it

### Infrastructure Layer
Allowed:
- catch technical exceptions
- translate technical exceptions into application exceptions
- let truly unexpected exceptions bubble up when translation is not meaningful

Infrastructure must not leak raw provider exceptions outside its boundary unless there is no meaningful domain/application translation.

---

## Translation Rules

Translate only when it improves the contract.

Examples:
- provider-specific "record not found" -> `NotFoundException`
- optimistic concurrency failure -> `ConflictException`
- timeout in a transient remote dependency -> application exception with `IsTransient = true`

Do not translate:
- one meaningful domain exception into another equally meaningful domain exception for no reason
- every unexpected exception into a fake business exception

Unknown failures must remain unknown failures and be handled by global error handling.

---

## Naming Rules

Exception names must:
- describe the violated rule or interaction failure
- be specific
- end with `Exception`

Prefer:
- `BuildingAlreadyUnderConstructionException`
- `NegativeOrZeroDurationException`
- `SettlementNotFoundException`

Avoid:
- `InvalidBuildingException`
- `BusinessException`
- `GeneralDomainException`

---

## Error Codes

Error codes must:
- be stable over time
- be lowercase
- be machine-readable
- not depend on the exception class name
- follow a consistent namespace-like pattern

Recommended pattern:

`<area>.<reason>`

Examples:
- `building.already_under_construction`
- `building.invalid_transition`
- `resource.not_found`
- `access.forbidden`
- `request.validation_failed`
- `request.unprocessable`
- `system.unexpected_error`

The error code is a contract. Renaming it is a breaking change.

---

## Design Guidelines

### 1. Use specific exception types

Avoid:

```csharp
throw new Exception("Invalid operation");
```

Prefer:

```csharp
throw new NegativeOrZeroDurationException(duration);
```

### 2. Do not use exceptions for normal control flow

Do not throw exceptions for expected branching where a simple domain method or query is enough.

### 3. Guard invariants early

Domain objects must reject invalid state transitions immediately.

### 4. Keep messages human-readable

Messages should help the caller or operator understand the failure.
They should not contain stack traces, provider messages, or low-level implementation details.

### 5. Keep technical context outside the message

Technical details belong in logs, trace attributes, or exception inner exceptions, not in the public business message.

---

## Anti-Patterns

- throwing `Exception`
- throwing HTTP-specific exceptions from the domain
- mixing validation and business rules
- translating every failure into `ConflictException`
- exposing provider-specific messages as public messages
- changing error codes casually
- logging the same exception in multiple layers

---

## Summary

- Domain exceptions express business rule violations
- Application exceptions express boundary and interaction failures
- Every exception has a stable error code
- Error codes are reused by API, logs, metrics, and traces
- Exceptions are part of the public and operational contract of the system
