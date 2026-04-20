# Worker Service Instructions

## Purpose

A Worker service is a host project responsible for running long-lived application behavior through the .NET Generic Host. It owns hosting, lifecycle, composition, and runtime wiring. It is not the place for domain logic, business rules, or technology-specific processing details that belong to core or infrastructure layers.

## Responsibilities

A Worker service may contain:

* `Program.cs` with Generic Host bootstrapping
* host configuration
* dependency injection composition root
* configuration binding
* logging and telemetry wiring
* resilience policy wiring
* hosted service implementations such as `BackgroundService`
* startup and shutdown coordination
* support for execution as console process, Windows Service, or containerized process when relevant

A Worker service must not contain:

* domain rules
* business decisions
* message transformation logic
* persistence logic
* queue protocol logic
* direct orchestration details that can live in reusable core services
* infrastructure code that should be reusable outside this host

## Design Principles

### Keep hosting separate from application logic

The Worker project is a runtime host. It must delegate actual work to services defined in core/application layers. The Worker starts the process, resolves dependencies, forwards cancellation tokens, and handles lifecycle boundaries. It should not become the place where the system behavior is implemented.

### Keep the hosted service thin

A hosted service should act as a bridge between the Generic Host and a reusable runtime service. It should not contain large amounts of operational logic. Its job is to call a core service, pass the cancellation token, and react correctly to startup and shutdown events.

### Treat the Worker as a composition root

The Worker project is responsible for assembling the system. This includes registering services, loading configuration, selecting infrastructure implementations, and applying cross-cutting concerns. It should compose the runtime, not redefine the runtime.

### Support multiple hosts

Core services must be reusable from other hosts such as a CLI, tests, or an API process. Worker-specific code must therefore remain isolated in the Worker project.

## Recommended Structure

A Worker project should typically contain:

* `Program.cs`
* one or more hosted services such as `BarWorker`
* service registration extensions specific to the host
* host-level configuration files

It may depend on:

* `Foo.Core`
* one or more infrastructure projects

It should not force other projects to depend on hosting packages.

## Program Setup

Use the .NET Generic Host as the default hosting model.

Typical responsibilities of `Program.cs`:

* create the host with `Host.CreateDefaultBuilder(args)`
* load configuration from appsettings, environment variables, and secrets when appropriate
* configure logging
* configure telemetry
* register core and infrastructure services
* register hosted services
* enable Windows Service integration when required
* build and run the host

Avoid putting business logic directly in `Program.cs`.

## Hosted Service Guidance

Hosted services should derive from `BackgroundService` unless another host integration pattern is clearly better.

A hosted service should:

* resolve and invoke a reusable runtime service
* pass the provided cancellation token
* log lifecycle events at an appropriate level
* fail fast on startup errors unless explicit recovery behavior exists
* stop cleanly on cancellation

A hosted service should not:

* implement the full ingestion workflow itself
* create infrastructure clients manually when DI can provide them
* parse configuration ad hoc instead of using bound options
* hide failures silently

## Dependency Injection

The Worker must use dependency injection as the composition mechanism.

Guidance:

* register core services through extension methods
* register infrastructure services through extension methods
* keep host registration readable and explicit
* avoid long registration blocks in `Program.cs` when they can be grouped into dedicated extension methods
* prefer constructor injection
* avoid service location patterns

Typical shape:

* `services.AddFooCore()`
* `services.AddFooRabbitMq()`
* `services.AddFooStorage()`
* `services.AddHostedService<BarWorker>()`

## Configuration

All host-specific configuration must be externalized and bound through options.

Guidance:

* use strongly typed options
* validate options at startup when possible
* keep host concerns in host configuration and reusable concerns in shared option types
* do not scatter raw configuration access across the codebase
* do not hardcode operational values in the Worker

The Worker is responsible for loading configuration. Core services are responsible only for consuming already-bound options.

## Logging

The Worker must configure logging, but log message content should remain meaningful and operational.

Guidance:

* log startup and shutdown boundaries
* log major runtime transitions
* log failures with enough context for diagnosis
* avoid duplicate logging at multiple layers for the same failure
* do not use the Worker as a dumping ground for verbose technical traces

Worker logs should help answer:

* did the host start correctly
* did the runtime begin correctly
* did the service stop cleanly
* where did startup or shutdown fail

## Telemetry

Telemetry setup belongs to the host or dedicated infrastructure registration, not to the core business logic.

Guidance:

* configure meters, tracing, and exporters outside the core runtime behavior
* keep telemetry wiring out of domain and application classes unless abstractions justify it
* prefer injecting narrow telemetry abstractions into core services when instrumentation is needed
* avoid direct OpenTelemetry setup inside hosted service execution code

## Resilience

Resilience policy wiring belongs in the host and infrastructure composition.

Guidance:

* define retries, timeouts, and circuit breakers through reusable policies
* register these policies centrally
* avoid ad hoc retry loops in hosted services
* make cancellation-aware behavior a default
* fail clearly when startup dependencies are unavailable and recovery is not intended

## Shutdown Behavior

A Worker service must stop gracefully.

Guidance:

* respect the host cancellation token
* stop accepting new work when shutdown begins
* allow in-flight work to finish according to the runtime rules
* release external resources correctly
* avoid abrupt process termination unless explicitly required

Shutdown behavior must be deliberate and testable.

## Error Handling

A Worker must distinguish between startup failures, runtime failures, and shutdown failures.

Guidance:

* startup failures should prevent the host from running successfully
* runtime failures should be logged and either trigger controlled termination or explicit recovery, depending on the design
* cancellation should not be treated as an error
* shutdown failures should be visible in logs

Do not swallow exceptions simply to keep the process alive.

## Hosting Variants

The Worker must remain a pure hosting composition regardless of how it is executed.

Guidance:

* hosting integrations such as Windows Service or containers are configuration concerns of the host
* these integrations must not change the responsibilities or structure of the Worker
* the same executable must be able to run consistently across environments (console, service, container)
* avoid branching logic in code based on hosting environment

The Worker is a single concern: hosting and running the application lifecycle. Different execution environments must not introduce additional responsibilities.

## Container Support

If the Worker is intended to run in containers:

* keep container concerns at the host level
* ensure logs go to standard output when appropriate
* avoid assumptions that only hold for Windows Services
* keep shutdown responsive to container stop signals

## Testing Expectations

The Worker project should have focused tests around hosting behavior, not business rules.

Examples:

* host starts with valid configuration
* host fails with invalid configuration
* hosted service delegates to the runtime service
* cancellation stops the runtime correctly
* service registration composes successfully

Business behavior should be tested in core or infrastructure test projects, not through Worker-only tests by default.

## Naming Guidance

Use names that clearly express hosting intent.

Preferred examples:

* `Foo.Worker`
* `BarWorker`
* `IBarRuntime`
* `WorkerServiceCollectionExtensions`

Avoid names that blur host and business responsibilities.

## Architectural Rule

The Worker answers the question: how does the process run?

Core answers the question: what does the system do?

Infrastructure answers the question: how does the system talk to external technology?

Keep these boundaries explicit in code, project structure, and dependencies.
