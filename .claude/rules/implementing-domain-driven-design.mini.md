# OBEY Implementing Domain-Driven Design by Vaughn Vernon

Use when DDD choices affect bounded contexts, language, aggregates, repositories, events, application services, package structure, or cross-context integration. Practical DDD is not renamed CRUD.

## Core rules

- Name the Bounded Context before interpreting terms, modules, APIs, or persistence; never force one global company model.
- One concept, one term inside the context; code, tests, events, and packages speak that Ubiquitous Language. Qualify, split, or rename ambiguous or drifting terms before coding further.
- Spend modeling effort on the Core Domain; keep supporting subdomains and trivial CRUD simple, and model invariants honestly once they appear.
- Translate foreign, legacy, vendor, partner, and infrastructure models into the local language at the boundary; never let their schemas, statuses, or enums into domain objects.
- Aggregates are small immediate-consistency boundaries with one root: route invariant-changing behavior through the root, hide mutable internals, no arbitrary setters, and no outside code reading internals to decide state changes.
- Reference other Aggregates by identity and default to one Aggregate per transaction. If a transaction wants several, list the invariants that require it; otherwise coordinate with events, policies, or Application Services.
- Entities carry identity and lifecycle and guard meaningful transitions. Value Objects are immutable, validated at construction, compared by value, and replace meaningful primitives (identifiers, quantities, ranges, names).
- Use Domain Services only for domain operations that span several objects and fit no Entity or Value Object; keep serialization, transport, and persistence mapping out of the domain.
- Repositories serve Aggregate Roots, not tables: domain-shaped interfaces, domain results, no business rules, no generic CRUD.
- Application Services and controllers coordinate use cases (load, invoke, persist, publish) and hold no branching business rules; move those decisions into the domain object that owns the concept.

## Read before working on

- Context relationships, Anticorruption Layers, cross-context imports, or module layout: `.claude/docs/ddd/context-integration.md`
- Domain Events or Event Sourcing: `.claude/docs/ddd/events-and-event-sourcing.md`
- DTOs, projections, read models, CQRS, or scope identifiers: `.claude/docs/ddd/read-models-and-queries.md`
- Generating a new model, writing domain tests, or reviewing DDD code: `.claude/docs/ddd/review-checklist.md`
