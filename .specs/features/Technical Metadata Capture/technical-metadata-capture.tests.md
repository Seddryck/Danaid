# Tests for Technical Metadata Capture

Feature: Technical Metadata Capture

## Unit tests

- [x] Maps RabbitMQ message identifier into technical metadata when present
- [x] Maps RabbitMQ correlation identifier into technical metadata when present
- [x] Maps routing key and exchange into technical metadata
- [x] Captures headers into a normalized storable structure
- [x] Handles missing optional transport fields without failing capture
- [x] Sets capture timestamp in UTC at capture time
- [x] Keeps technical metadata separate from payload interpretation

## Interaction tests

- [x] Capture flow sends raw payload and technical metadata together to persistence
- [x] Persistence request includes message/correlation identifiers when available
- [x] Missing metadata fields are forwarded as null/empty without fallback business mapping

## Integration tests

- [x] Persisted record contains raw payload and a distinct technical metadata section
- [x] Persisted technical metadata includes message ID, correlation ID, routing key, exchange, headers, and capture timestamp when available
- [x] Persisted record remains valid when some broker metadata is absent
- [x] Trace/correlation identifiers are preserved for diagnostics and replay support
- [x] (Contract) Stored metadata contract remains stable (field names and shape) for replay and diagnostics tooling
- [x] (Contract) Missing transport metadata does not break the persisted contract shape
- [x] (Serialization) Headers with mixed value types are serialized into the normalized metadata structure predictably
- [x] (Serialization) Persisted technical metadata can be deserialized without losing identifiers or timestamps
- [x] (Serialization) Serialization preserves UTC capture timestamp semantics
