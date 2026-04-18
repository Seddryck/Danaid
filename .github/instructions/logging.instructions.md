# Logging Guidance

## Goal

Keep application code readable while avoiding unnecessary work in logging paths.

This project does not require every log statement to use source-generated logging. The objective is to balance:
- readability of the main code
- log quality
- performance
- compliance with analyzer rules such as CA1873

## General rule

Use the simplest logging approach that keeps the code clear and avoids unnecessary computation.

## Standard logging

Use normal structured logging for simple and cheap log statements.

Preferred example:

_logger.LogInformation("Started processing message {MessageId}", messageId);

This is the default choice when:
- log arguments are already available
- no expensive formatting is needed
- no serialization, projection, join, or preview generation is required

## Expensive logging

When a log statement requires expensive computation, the computation must not happen unless the corresponding log level is enabled.

Typical expensive operations include:
- serializing objects
- building payload previews
- string concatenation over large content
- LINQ materialization used only for logging
- computing counts, summaries, hashes, or projections only for diagnostics

In such cases, guard the log with `IsEnabled`.

Preferred example:

if (_logger.IsEnabled(LogLevel.Debug))
{
    var preview = BuildPayloadPreview(payload);
    _logger.LogDebug("Payload preview for {QueueName}: {PayloadPreview}", queueName, preview);
}

## Source-generated logging

Use source-generated logging for repeated, important, or performance-sensitive messages.

## File organization for generated logs

Place logging methods in a dedicated companion file.

Naming convention:
- MyService.cs
- MyService.Log.cs

## Keep logs readable

Prefer logging:
- identifiers
- sizes
- counts
- truncated previews
- correlation ids

Avoid logging:
- full serialized objects
- complete message bodies

## Analyzer CA1873

Treat CA1873 as a design signal:
- guard expensive logs
- use source-generated logging when relevant
- suppress selectively when trivial

## Decision rules

1. Use normal structured logging for simple logs  
2. Use `IsEnabled` for expensive logs  
3. Use source-generated logging for repeated messages  
4. Place generated logs in `*.Log.cs`  

## Expected behavior for code generation

- prefer simple logging
- avoid unnecessary computation
- keep logs concise and structured
