# DDD: Read Models and Queries

Read before working on DTOs, projections, read models, CQRS, or scope identifiers. Extends `.claude/rules/implementing-domain-driven-design.mini.md`.

## Rules

- Use DTOs, projections, use-case queries, rendition adapters, or mediators when client needs differ from Aggregate shape; expose application-facing representations rather than aggregate internals.
- Keep command behavior separate from query models when consistency, performance, or representation needs justify it, and keep scope identifiers explicit where context or ownership affects invariants or access.

## Triggers

- When client rendering, query speed, or representation needs pressure the model shape, use projections, DTOs, use-case queries, or adapters instead of enlarging or exposing Aggregates.
