# Configurable Queue Consumption

The ingestion layer connects to RabbitMQ and consumes messages from predefined queues only, without applying any routing or filtering logic.

The capture service:

* listens to one or multiple configured queues
* consumes all messages as-is
* does not interpret exchanges or routing keys

Queue selection is fully configuration-driven, allowing:

* reuse of the same capture service across environments
* clear separation of ingestion responsibilities per queue