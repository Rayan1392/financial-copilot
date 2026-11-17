# DDD: Domain Events and Event Sourcing

Read before working on Domain Events or Event Sourcing. Extends `.claude/rules/implementing-domain-driven-design.mini.md`.

## Rules

- Publish Domain Events only for meaningful completed business facts; name them in the past tense, keep payloads local to the model, and do not use events for every property change or to hide poor Aggregate design.
- Use Event Sourcing only when the event sequence is the right persistence model; streams must match Aggregate identity and versioning, replay must be deterministic, and event meaning changes need versioning, upcasters, or translators.

## Triggers

- When an event reads like a command, exposes framework or persistence artifacts, or describes a minor property change, rename, narrow, or remove it.
