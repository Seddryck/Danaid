# Tests for Persistence Before Acknowledgement

Feature: Persistence Before Acknowledgement

## Unit tests

- [x] Ack happens only after successful persistence
- [x] Persistence failure leads to nack/requeue and no ack
- [x] Mixed batch outcomes follow defined policy

## Integration tests

- [x] Redelivery after storage failure preserves at-least-once behavior
- [x] Crash/restart scenarios do not lose unpersisted messages
