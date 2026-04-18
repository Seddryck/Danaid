# resilience.instructions.md

## Purpose

These instructions define how resilience must be handled in this codebase.

Resilience is about dealing with technical uncertainty at system boundaries. It covers transient faults, slow dependencies, temporary unavailability, and overload situations. It does not replace correct domain modeling, and it must never be used to hide functional defects, broken contracts, or poor operational discipline.

Resilience policies must be explicit, intentional, and observable. They must be applied where failures can actually happen: at infrastructure and integration boundaries, not inside the domain model.

This codebase must use **Polly** as the standard library for resilience mechanisms such as retries, circuit breakers, and timeouts.

## Core principles

### Keep resilience at the edges

Resilience belongs at technical boundaries:
- outbound HTTP calls
- message broker interactions
- database calls when appropriate
- external APIs
- storage services
- other infrastructure adapters

Do:
- apply resilience where the system interacts with unreliable external or infrastructural dependencies
- keep resilience policies close to those boundaries
- make policy usage visible in composition and adapter code

Do not:
- add retries inside entities, value objects, or domain services
- scatter ad hoc retry loops throughout business code
- treat resilience as a substitute for fixing bugs

### Distinguish technical failure from business failure

Retries, circuit breakers, and timeouts address technical faults. They do not address business rejections.

Do:
- retry transient network or transport failures when there is a realistic chance of recovery
- fail fast on validation errors, business rule violations, authentication failures, or malformed requests
- classify failures before applying resilience

Do not:
- retry a request that is invalid
- retry a business rule violation
- retry because “something failed” without understanding the failure category

### Prefer explicit policies over hidden behavior

Resilience behavior must be deliberate and reviewable.

Do:
- define named resilience policies
- apply them through clearly identified infrastructure code
- keep policy parameters understandable and justified

Do not:
- hide retry loops in helper methods without making them visible
- use magic defaults without review
- let every developer invent their own strategy for common dependencies

### Favor predictable failure over uncontrolled waiting

A slow failure is often worse than a fast, explicit one. Time must be bounded.

Do:
- define timeouts for external calls
- combine timeouts with cancellation support
- ensure callers can understand when a dependency is unavailable or too slow

Do not:
- wait indefinitely on external systems
- accumulate nested timeouts without thought
- assume the underlying client defaults are sufficient

## Standard library

This codebase must use **Polly** for resilience concerns.

Use Polly for:
- retries
- circuit breakers
- timeouts
- fallback only when there is a genuine, meaningful fallback
- bulkhead isolation when justified by the architecture

Do not introduce multiple resilience frameworks for the same concerns unless there is a very strong reason and it has been explicitly approved.

Polly must be the default and shared approach so that resilience remains consistent across services and adapters.

## Architectural rules

### Domain layer

The domain layer must not know about resilience mechanisms.

Do:
- keep domain logic focused on business invariants and decisions
- surface domain failures explicitly

Do not:
- place Polly policies in domain entities or value objects
- inject resilience policies into the domain
- encode retries or circuit breakers into business behavior

### Application layer

The application layer may orchestrate work, but resilience logic should usually remain in the outbound adapter or client configuration.

Do:
- let application services depend on abstractions that already encapsulate resilient access to external systems
- propagate cancellation tokens
- make failure handling explicit at use-case level when needed

Do not:
- wrap the same operation repeatedly with new resilience policies at several layers
- duplicate resilience concerns in every handler
- turn application services into policy factories

### Infrastructure layer

Infrastructure is where resilience is usually implemented.

Do:
- apply Polly policies in HTTP clients, repository adapters, messaging adapters, storage adapters, and other outbound connectors
- centralize reusable policy definitions
- keep policy wiring readable and close to composition

Do not:
- bury resilience behavior deep in private utility code where nobody can see it
- mix unrelated policies without documenting intent
- apply the same retry logic blindly to all dependencies

### Composition root

The host or composition root must make resilience choices visible.

Do:
- register and configure Polly-based policies during startup or infrastructure registration
- name or group policies according to dependency type or use case
- keep resilience configuration discoverable

Do not:
- leave policy choice implicit
- spread resilience setup randomly across the codebase
- make operations resilient by accident

## Retry guidance

Retries must be used carefully.

### When retries are appropriate

Retries are appropriate when:
- the failure is transient
- the target operation is safe to retry or has explicit idempotency guarantees
- a short delay can realistically help

Common examples:
- temporary network interruption
- DNS or connection glitches
- short-lived HTTP 5xx errors
- temporary service unavailability
- throttling responses when backoff is appropriate

### When retries are not appropriate

Do not retry:
- validation failures
- authentication or authorization failures
- contract mismatches
- domain rule violations
- malformed payloads
- operations with unsafe side effects unless idempotency is guaranteed
- long-running failures where repeated load will worsen the situation

### Retry rules

Do:
- use bounded retry counts
- prefer backoff over immediate tight loops
- use jitter when multiple instances may retry simultaneously
- log and measure retry activity
- ensure the operation is idempotent before retrying

Do not:
- retry forever
- retry instantly in hot loops
- chain multiple retry mechanisms on top of each other
- retry non-idempotent operations without deliberate safeguards

### Idempotency matters

Before adding retries, verify whether the target operation can be repeated safely.

For commands with side effects, consider:
- idempotency keys
- deduplication mechanisms
- message processing safeguards
- transactional boundaries
- outbox or inbox patterns where relevant

A retry without idempotency analysis is incomplete design.

## Circuit breaker guidance

Circuit breakers protect the system from repeatedly calling a failing dependency.

Use a circuit breaker when:
- a dependency is repeatedly failing
- continuing to call it would waste resources
- fast rejection is better than slow collapse
- recovery should be probed after a break period

Do:
- use circuit breakers for unstable external dependencies
- choose thresholds deliberately
- emit logs, metrics, and traces for open and half-open states
- make failure modes visible to operators

Do not:
- use circuit breakers as a decorative default everywhere
- set thresholds arbitrarily without considering traffic patterns
- hide open-circuit failures from the caller

Circuit breakers are especially useful when combined with retries and timeouts, but the combination must be reasoned about. A badly ordered or badly tuned combination can amplify failure rather than contain it.

## Timeout guidance

Every external operation should have an intentional time budget.

Do:
- define timeouts explicitly
- pass cancellation tokens through the call chain
- align timeout budgets with user expectations, API contracts, and background processing requirements
- think in terms of end-to-end latency budgets, not isolated local defaults

Do not:
- rely blindly on default client timeouts
- stack several unrelated timeouts without understanding the combined effect
- let background processing block forever on an unavailable dependency

Timeouts should be chosen at the right level:
- per call, when a specific operation has a clear budget
- per client or dependency, when several operations share similar characteristics

## Policy composition

Combining policies is often necessary, but it must stay understandable.

Typical order must be reasoned explicitly. In many outbound scenarios, a sensible composition may include:
- timeout to bound each try
- retry for transient failure
- circuit breaker to stop repeated calls to a failing dependency

The exact composition depends on the client and the dependency. The key requirement is clarity.

Do:
- define standard policy compositions for standard dependency categories
- document the intent of each composition
- keep the number of combined policies limited and comprehensible

Do not:
- build deep policy stacks that nobody can explain
- mix fallback, retry, circuit breaker, timeout, and bulkhead in one place without strong justification

## Polly usage guidance

### Preferred approach

Use Polly as the shared implementation mechanism for resilience.

Do:
- define reusable policy builders or resilience pipelines for common dependency types
- configure Polly in infrastructure registration or client setup
- keep policy code local to infrastructure and composition concerns
- use typed or named client registrations where that improves clarity

Do not:
- instantiate Polly policies ad hoc in arbitrary business methods
- duplicate nearly identical policies throughout the solution
- let every class build its own resilience configuration

### Example intent

Examples of valid uses:
- an outbound HTTP client configured with Polly retry, timeout, and circuit breaker policies
- a storage adapter using Polly to handle transient transport issues
- a message publisher using Polly for transient broker connectivity issues where retry is safe

Examples of invalid uses:
- a domain service retrying a business decision
- an entity using Polly to fetch missing data
- a command handler wrapping every internal method call in a resilience policy

## Messaging and asynchronous processing

Retries in messaging require extra care because the transport may already provide delivery and redelivery mechanisms.

Do:
- distinguish between application-level retry and broker-level redelivery
- understand the semantics of the transport before adding Polly retries around message consumption or publication
- handle poison messages explicitly
- use dead-lettering, parking, or quarantine strategies where appropriate

Do not:
- layer transport retry, application retry, and consumer retry blindly
- retry malformed or poison messages forever
- hide repeated failures from operations teams

For message handling, resilience design must consider:
- idempotent consumption
- deduplication
- redelivery behavior
- dead-letter policies
- observability of repeated failures

## Data access considerations

Not every database operation should be retried.

Do:
- assess whether the storage technology and operation type exhibit genuine transient faults
- understand transaction boundaries before retrying
- ensure retries do not duplicate side effects or break consistency assumptions

Do not:
- apply generic retry rules to all persistence code
- retry write operations without considering transaction semantics
- assume a retry is harmless just because the failure came from infrastructure

## Fallback guidance

Fallback is allowed only when there is a real degraded mode with acceptable semantics.

Do:
- use fallback when the business can accept a clearly defined degraded answer
- make degraded behavior explicit
- log and measure fallback usage

Do not:
- use fallback to silently hide serious errors
- return fake success
- degrade data correctness without visibility

A fallback must be a real business-acceptable alternative, not a way to mask instability.

## Observability

Resilience without observability is unsafe.

Do:
- log retries when useful, without flooding logs
- emit metrics for retries, timeout occurrences, open circuits, and fallback usage
- trace outbound calls and failure paths
- keep telemetry supportive rather than invasive

Do not:
- hide resilience behavior
- produce noisy logs for every transient blip without aggregation strategy
- let resilience policies operate without operational visibility

Resilience policies must help operators answer:
- what is failing
- how often retries occur
- whether the circuit is open
- whether timeouts are happening
- whether the system is degrading gracefully or collapsing

## Configuration

Policy parameters may depend on the dependency, but configuration must remain explicit and typed.

Do:
- bind policy settings from typed options where variability is needed
- validate resilience settings at startup
- keep defaults conservative and understandable

Do not:
- scatter retry counts and timeout durations as magic numbers
- expose endless tuning knobs without operational purpose
- let configuration override sound design

## Anti-patterns to avoid

Do not:
- use retries as a substitute for fixing broken code
- retry non-idempotent operations blindly
- put Polly in the domain layer
- retry validation or business rule failures
- stack multiple retry systems on the same call path without analysis
- use infinite retries
- omit timeouts
- open a circuit with thresholds nobody understands
- add fallback that returns misleading data
- create custom resilience behavior everywhere instead of standardizing on Polly

## Recommended style for this codebase

In this codebase:
- **Polly is the standard resilience library**
- resilience must be implemented at infrastructure boundaries
- retries must be bounded and used only for transient faults
- timeouts must be explicit
- circuit breakers must protect unstable dependencies
- resilience must be observable
- idempotency must be checked before retrying side-effecting operations
- messaging resilience must account for broker semantics and dead-letter handling
- resilience code must remain readable from the composition root and infrastructure registration

## Review checklist

When reviewing resilience design, check the following:
- Is **Polly** used as the resilience mechanism?
- Is resilience placed at technical boundaries rather than in the domain?
- Are retries limited to transient faults?
- Has idempotency been considered before retrying side effects?
- Are timeouts explicit?
- Are circuit breakers justified and observable?
- Is policy composition understandable?
- Are messaging semantics considered separately from simple request/response calls?
- Are configuration values typed and validated?
- Does the resilience design clarify failure handling rather than hide it?
