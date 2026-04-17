# Principles

## Ingestion Completeness

If a message is present in the configured queue, it must be ingested.
The ingestion layer does not decide which messages are relevant. Queue definition sets the ingestion scope.

## Separation of Ingestion and Processing

Ingestion is responsible for receiving and persisting messages, not for applying business logic.
Filtering, interpretation, enrichment, and transformation belong downstream.

## Durability Before Acknowledgement

A message must not be considered successfully consumed before it has been durably persisted.
Acknowledgement reflects successful capture, not just successful receipt.

## Storage as Source of Truth

RabbitMQ is a transport mechanism, not the long-term system of record.
The durable persisted output is the authoritative basis for replay, recovery, and downstream processing.

## Explicit Delivery Semantics

The ingestion layer must make delivery guarantees explicit.
It should not hide whether the system is at-most-once, at-least-once, or effectively-once through idempotency.

## Idempotency by Design

Duplicate delivery is a normal condition in distributed ingestion and must be handled explicitly.
The system must preserve stable identifiers that allow safe deduplication.

## Operational Simplicity

The ingestion model should favor predictable and observable behavior over overly clever abstractions.
Scaling, troubleshooting, and deployment should remain straightforward.

## Configuration Over Hardcoding

Operational parameters such as queue name, vHost, connection settings, and batching thresholds must be externalized.
The same implementation should be reusable across environments without code changes.

## Observability as a First-Class Concern

Ingestion must expose enough information to understand what was received, persisted, retried, acknowledged, or failed.
A capture pipeline that cannot be observed cannot be trusted.

Controlled Resource Usage

Ingestion must operate within explicit bounds for memory, concurrency, and in-flight messages.
Throughput should be tuned deliberately, not by letting the runtime grow unchecked.
