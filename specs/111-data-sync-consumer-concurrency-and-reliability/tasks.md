# Tasks — Data-Sync Consumer Concurrency and Reliable Delivery

## 1. Consumer Concurrency

- [x] Add a validated `DataSyncMessaging:ConsumerCount` option for the Worker.
- [x] Start the configured number of independent consumers for `financialcopilot.data-sync.requests`.
- [x] Keep each consumer scoped to one message at a time and preserve isolated DI scopes per processing attempt.
- [x] Document the safe operating range and recommended initial deployment value.

## 2. Reliable Acknowledgement and Retry

- [x] Acknowledge a RabbitMQ data-sync message only after its processing attempt has persisted a terminal result.
- [x] Requeue a message when processing ends unexpectedly before a terminal result can be persisted.
- [x] Reject malformed messages without requeueing them.
- [x] Preserve idempotency so redelivery cannot create duplicate `ProviderSyncRuns` records.

## 3. Observability and Tests

- [x] Log consumer start/stop and message-processing failures with request identifiers.
- [x] Unit-test option validation and bounded multi-consumer startup.
- [x] Unit-test ACK-after-success and NACK/requeue-on-unexpected-failure semantics.
- [x] Verify an existing failed `NoDataYet` run remains safe to retry.

## Completion Gate

- [x] Parallel consumers, acknowledgement ordering, redelivery safety, and focused tests pass.
