# Dependency Injection Instructions

## Purpose

These instructions define how dependency injection must be used in this codebase.

Dependency injection is an assembly concern. Its purpose is to wire the application from the outside, not to leak framework mechanics into the domain model or to compensate for unclear responsibilities.

The dependency graph must make architecture visible:
- domain stays independent from dependency injection frameworks
- application depends on abstractions and explicit use cases
- infrastructure provides implementations
- composition root wires everything together

Dependency injection must improve clarity, replaceability, and testability. It must not become a hidden service locator, a dumping ground for unrelated registrations, or a way to bypass proper modeling.

## Core principles

### Keep dependency injection at the edges

Dependency injection belongs to the composition root and infrastructure setup. Business code must not know about containers, service providers, registration APIs, or framework-specific lifetimes.

Do:
- register services in dedicated composition code
- inject dependencies through constructors
- let the application start-up assemble the object graph

Do not:
- inject `IServiceProvider`
- resolve services manually from the container in business code
- pass the container around
- hide dependencies behind ambient resolution

### Depend on explicit abstractions

A class should depend on the collaborators it really needs. Constructor parameters must make those dependencies visible.

Do:
- inject ports, repositories, application services, policies, gateways, clocks, and other explicit collaborators
- keep interfaces aligned with architectural roles
- use one abstraction per responsibility

Do not:
- inject broad “manager”, “helper”, or “utility” services with mixed concerns
- inject dependencies that are only needed for one rare branch if they reveal missing decomposition
- inject infrastructure types directly into domain code

### Favor constructor injection

Constructor injection is the default. Required dependencies must be mandatory and visible at construction time.

Do:
- use constructor injection for all required dependencies
- fail fast when mandatory dependencies are missing
- keep constructors honest

Do not:
- use property injection
- use method injection as a default pattern
- make dependencies optional unless the domain or use case explicitly requires optional behavior

### Composition must reflect architecture

Registrations must follow architectural boundaries.

Typical direction:
- domain: no DI registration logic, no framework dependency
- application: handlers, app services, orchestrators, policies, ports
- infrastructure: repository implementations, adapters, external clients, serializers, message brokers
- API / host: composition root, configuration binding, registration orchestration

Dependency injection must not blur these layers.

## Architectural rules

### Domain layer

The domain layer must not depend on the DI framework.

Do:
- keep entities, aggregates, value objects, and domain services free from container concerns
- create domain objects directly when possible
- inject only domain-relevant collaborators into domain services when needed

Do not:
- register services from the domain layer
- annotate domain types for DI
- inject repositories, loggers, telemetry, configuration, or service providers into entities and value objects

A rich domain model should usually be instantiated directly, not resolved from the container.

### Application layer

The application layer may depend on abstractions and orchestrate use cases.

Do:
- register app services, command handlers, query handlers, validators, and policy evaluators
- inject repositories, unit-of-work abstractions, clocks, identity/context abstractions, and outbound ports
- keep handlers focused on one use case

Do not:
- inject concrete infrastructure implementations when an abstraction is appropriate
- centralize unrelated use cases into giant services
- use DI to hide poor decomposition

When multiple policies apply to a use case, prefer injecting a clearly named collection of policy implementations only when the model explicitly calls for pipeline-like evaluation. Do not create “bags of services” without a clear contract.

### Infrastructure layer

Infrastructure provides implementations for application and domain abstractions.

Do:
- register concrete adapters in infrastructure modules
- keep external client setup, serializers, transport bindings, and persistence implementations here
- isolate third-party libraries behind local abstractions when they are part of the core architecture

Do not:
- let framework-specific client types spread everywhere
- expose raw external SDK usage through unrelated layers unless there is a deliberate reason
- use infrastructure registration as a substitute for explicit architecture

### API and host layer

The host layer owns composition.

Do:
- keep a clear composition root
- bind configuration objects here
- call registration extensions for application and infrastructure
- validate configuration early
- keep endpoint code independent from container mechanics

Do not:
- duplicate registrations in multiple places
- mix endpoint mapping and low-level wiring in the same block when it hurts readability
- let startup code become an unstructured list of registrations

## Registration guidelines

### Group registrations by responsibility

Registrations should be organized into explicit methods or modules such as:
- application services
- persistence
- messaging
- telemetry
- authentication/authorization
- external clients

This organization must reflect architecture, not arbitrary technical categories.

Prefer methods such as:
- `AddApplication()`
- `AddInfrastructure()`
- `AddPersistence()`
- `AddMessaging()`

These methods must remain readable and intention-revealing.

### Register abstractions, not usage shortcuts

Register services according to their architectural role.

Prefer:
- `ISettlementRepository -> SettlementRepository`
- `IClock -> SystemClock`
- `IProfanityPolicy -> RegexProfanityPolicy`

Avoid registrations whose names hide the role of the service.

### Avoid duplicate or conflicting registrations

A service should have one obvious registration strategy. Multiple competing registrations for the same responsibility make the system harder to reason about.

Use multiple implementations only when the business model or application pipeline explicitly requires it.

### Prefer explicit registration over magic

Assembly scanning and automatic registration may be used carefully, but explicit registration is preferred when it improves readability and predictability.

Use scanning only when:
- conventions are stable
- scope is limited
- the resulting graph stays understandable

Do not use scanning to avoid thinking about architecture.

## Lifetime guidance

Lifetimes must reflect behavior and resource ownership, not habit.

### Singleton

Use singleton only for stateless, thread-safe services or for components that intentionally represent one shared instance.

Examples may include:
- clocks, when modeled that way
- serializers without mutable state
- metadata providers
- some caches or registries, if designed for concurrent shared access

Do not make a service singleton if it depends on scoped or transient state.

### Scoped

Use scoped lifetime for components that should live for one application scope, often one request or one message-processing scope.

Examples may include:
- units of work
- request context services
- repositories when tied to a scoped persistence context

### Transient

Use transient lifetime for lightweight, stateless services created on demand.

Examples may include:
- handlers
- pure domain services hosted by the application layer
- mappers without shared mutable state

### Lifetime rules

Do:
- verify that dependencies flow from longer-lived services to equally long-lived or longer-lived dependencies only when safe
- think explicitly about thread safety
- keep state ownership clear

Do not:
- inject scoped services into singletons
- choose singleton by default for performance folklore
- keep mutable request-specific state in shared services

## Logging, telemetry, and configuration

Dependency injection must not make transversal concerns invade the model.

### Logging

Inject logging only where operational insight is needed, typically in application services, handlers, background workers, and infrastructure adapters.

Do not inject loggers into entities or value objects.

### Telemetry

Telemetry must stay behind dedicated abstractions when the raw telemetry primitives would dominate the code or couple the class too strongly to a specific instrumentation stack.

Do:
- keep instrumentation wiring in composition and infrastructure
- inject dedicated telemetry abstractions when needed by core components

Do not:
- spread metrics primitives through all constructors without need
- let instrumentation shape the business API

### Configuration

Configuration should be bound to explicit options objects at the edge.

Do:
- bind configuration once
- validate options early
- inject typed options or already-validated configuration abstractions into services that need them

Do not:
- inject raw configuration everywhere
- read configuration ad hoc from deep business code
- let string keys leak across the codebase

## Collections and pipelines

Injecting multiple implementations can be valid when the model is explicitly composite.

Appropriate examples:
- message enrichment pipeline
- validation rule set
- policy chain
- startup contributors

Rules:
- the consuming class must define the contract clearly
- execution order must be explicit when order matters
- each implementation must have one clear responsibility

Do not use `IEnumerable<T>` injection as a generic extension hook without boundaries.

## Factories and deferred creation

Use factories only when object creation truly depends on runtime data or when a shorter-lived object must be created within a longer-lived component through a controlled abstraction.

Prefer:
- explicit factory interfaces
- small, intention-revealing factory methods

Avoid:
- resolving arbitrary services from `IServiceProvider`
- service locator patterns disguised as factories

A factory is acceptable when it preserves explicit dependencies. It is not acceptable when it becomes a back door to the container.

## Testing implications

Dependency injection must support testing by making dependencies replaceable and visible.

Do:
- construct classes directly in unit tests when possible
- substitute dependencies with test doubles at the abstraction boundary
- keep constructors small enough to remain test-friendly

Do not:
- require the container for ordinary unit tests
- use DI to hide too many collaborators
- accept bloated constructors without questioning the class design

A large constructor is often a design signal. The first reaction should be to review responsibilities, not to look for a more clever DI trick.

## Anti-patterns to avoid

Do not:
- use the container as a service locator
- inject `IServiceProvider` into application code
- inject optional dependencies to mask weak design
- make everything singleton
- register concrete classes everywhere without architectural intent
- let entities depend on infrastructure concerns
- put business branching in registration code
- use named registrations or keyed resolution as a substitute for proper modeling unless there is a clear architectural need
- hide side effects behind decorators that nobody can see from the composition root

## Recommended style for this codebase

In this codebase:
- domain objects should usually be created directly
- app services and handlers should depend on explicit ports and repositories
- infrastructure adapters should be registered in dedicated infrastructure extensions
- options should be typed and validated at startup
- logging and telemetry should remain supportive concerns, not dominant constructor noise
- the composition root should be readable enough to explain the system structure

Dependency injection must make the architecture easier to read. If the registration model or constructor graph makes the system harder to understand, the design should be reconsidered.

## Review checklist

When reviewing dependency injection usage, check the following:
- Are dependencies explicit and constructor-based?
- Is the domain free from DI framework concerns?
- Are abstractions aligned with architectural roles?
- Are lifetimes justified?
- Is configuration bound and validated at the edge?
- Are logging and telemetry kept out of the domain model?
- Is the composition root readable?
- Does DI clarify the architecture rather than hide it?
