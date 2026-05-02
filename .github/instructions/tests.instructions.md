# Testing Guidance

## Purpose

This file defines the testing conventions for the repository. It establishes a common vocabulary for test scopes, test concerns, naming, structure, and tooling. The goal is to keep tests consistent, easy to navigate, and explicit about what they validate.

## Approved test tooling

All tests must use the following baseline stack:

- **NUnit** for the test framework.
- **Moq** for mocking and verification of interactions.
- **No assertion library other than NUnit assertions.**

Do not introduce alternative test frameworks, mocking frameworks, or fluent assertion libraries unless an explicit repository decision says otherwise.

## Test scope taxonomy

Tests must be written and organized using the following scope definitions.

### Unit tests

Unit tests verify logic in isolation.

A unit test focuses on one small unit of behavior and replaces dependencies that are outside the scope of the test. The objective is to validate the logic of the unit itself, not the behavior of real external collaborators.

Typical characteristics:

- small scope
- fast execution
- no real technical integration
- dependencies replaced with mocks, stubs, or fakes as needed

### Interaction tests

Interaction tests verify how a unit or component collaborates with its dependencies.

These tests are the place to verify calls, arguments, call counts, and sequencing. If a test asserts that a dependency was invoked once, was invoked with specific data, or was not invoked under certain conditions, it is an interaction test.

Typical characteristics:

- focus on collaboration behavior
- uses **Moq** for verification
- checks calls, arguments, ordering, or absence of calls
- does not validate real technical effects across real components

### Integration tests

Integration tests verify that real components work correctly together.

They validate real integrations such as filesystem access, databases, messaging, serialization, HTTP layers, or framework pipelines. Assertions should primarily be about observable effects and outcomes, not about mock invocations.

Typical characteristics:

- multiple real components involved
- validates technical boundaries
- checks actual effects, persisted state, emitted telemetry, returned responses, produced messages, and similar outcomes
- mocks should be minimized and used only when a boundary truly cannot or should not be exercised

### End-to-end tests

End-to-end tests verify a complete business or user flow through the system from the outside.

They validate the system as a whole, or a near-whole slice of it, using its public entry points and checking externally visible outcomes.

Typical characteristics:

- broadest test scope
- exercises full flow from entry point to outcome
- validates behavior through public interfaces
- should remain limited in number and focused on critical scenarios

## Namespace conventions

Tests must use **distinct namespaces** for each test scope. Do not mix scopes in the same namespace.

Use the following namespace structure:

- `*.Testing.Unit`
- `*.Testing.Interaction`
- `*.Testing.Integration`
- `*.Testing.EndToEnd`

Examples:

- `Foo.Bar.Testing.Unit`
- `Foo.Bar.Testing.Interaction`
- `Foo.Bar.Testing.Integration`
- `Foo.Bar.Testing.EndToEnd`

If needed, deeper sub-namespaces may be added below these roots, but the scope segment must remain explicit.

Examples:

- `Foo.Bar.Core.Testing.Unit.Storage`
- `Foo.Bar.Core.Testing.Interaction.Consumers`
- `Foo.Bar.Api.Testing.EndToEnd.QueueConsumption`

## Test concerns

In addition to scope, tests may express a **test concern**. Concerns are orthogonal to scope. A test can be integration and observability, or unit and resilience, and so on.

Concerns must be expressed using NUnit categories.

Use the following concern categories:

- `Functional`
- `Observability`
- `Resilience`
- `Security`
- `Performance`
- `Compatibility`
- `Contract`
- `Serialization`
- `Configuration`

These categories describe **what the test is about**, not the scope of the test.

Examples:

- a unit test validating pure behavior may be categorized as `Functional`
- an interaction test verifying logging calls may be categorized as `Observability`
- an integration test validating metrics or traces may be categorized as `Observability`
- an integration test validating retry behavior may be categorized as `Resilience`
- an end-to-end test validating authentication and authorization may be categorized as `Security`

## Category usage rules

All tests should have:

- an explicit scope through their namespace
- at least one concern category through NUnit

Example:

```csharp
using NUnit.Framework;

namespace Danaid.Core.Testing.Integration;

[TestFixture]
[Category("Observability")]
public class FileSystemStorageWriterTests
{
}
```

A test may have more than one concern category when justified, but categories must remain deliberate and limited. Do not add categories mechanically.

Example:

```csharp
[Test]
[Category("Observability")]
[Category("Resilience")]
public async Task WriteAsync_EmitsErrorTelemetry_WhenPersistenceFails()
{
}
```

## Tooling rules by scope

### Unit tests

- prefer no mocks when the unit is naturally pure
- use **Moq** when collaborators must be replaced
- use NUnit assertions only
- do not hit real infrastructure

### Interaction tests

- use **Moq** to verify collaboration behavior
- assert calls, arguments, counts, and absence of calls
- do not use interaction tests to validate real infrastructure behavior
- if the test validates real effects instead of calls, it likely belongs to integration

### Integration tests

- prefer real implementations for the components under test
- avoid asserting that mocks were called as the primary proof of correctness
- assert observable outcomes
- use Moq only for boundaries that are intentionally excluded from the integration scope

### End-to-end tests

- exercise the system through real entry points
- assert externally visible outcomes
- keep mocking to an absolute minimum
- focus on critical journeys, not exhaustive permutations

## Naming guidance

Test class names and test method names should clearly communicate the behavior and scope being validated.

Prefer names that state:

- the unit, component, or flow under test
- the expected behavior or outcome
- the relevant condition when needed

Examples:

- `WriteAsync_PersistsBatchToDisk`
- `PublishAsync_CallsBrokerOnce_WhenMessageIsValid`
- `CreateOrder_ReturnsBadRequest_WhenPayloadIsInvalid`
- `QueueConsumption_StoresBatchAndEmitsTelemetry`

## Classification guidance

Use the following decision rules when classifying tests:

- if the test validates logic in isolation, it is **Unit**
- if the test validates calls to dependencies, it is **Interaction**
- if the test validates real components working together, it is **Integration**
- if the test validates a complete external flow through the system, it is **End-to-end**

When unsure between interaction and integration, use this rule:

- if the main assertion is about **calls**, it is usually **Interaction**
- if the main assertion is about **real effects**, it is usually **Integration**

## Example structure

Tests are kept next to the code:

```text
src/
  Foo.Core/
  Foo.Core.Testing/
    Unit/
    Interaction/
    Integration/
    EndToEnd/
```

## Final rule

Every test must make two things obvious:

- its **scope** through the namespace
- its **concern** through NUnit categories

This distinction must remain clear throughout the repository.


## Assertion style preference

Prefer the use of `Assert.That` over other NUnit assertion styles (`Assert.AreEqual`, `Assert.IsTrue`, etc.).

Rationale:
- provides a more expressive and readable constraint-based syntax
- enables richer assertions (collections, strings, exceptions, etc.)
- keeps consistency across the codebase

Preferred:

```csharp
Assert.That(result.Success, Is.True);
Assert.That(items, Has.Count.EqualTo(2));
Assert.That(message, Does.Contain("accepted"));
```

Avoid:

```csharp
Assert.IsTrue(result.Success);
Assert.AreEqual(2, items.Count);
```

`Assert.Multiple` can be used in combination with `Assert.That` when grouping related assertions.

Use Assert.Multiple when ready,

Allowed style:

```csharp
Assert.Multiple(() =>
{
    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    Assert.That(responseBody, Does.Contain("accepted"));
});
```
