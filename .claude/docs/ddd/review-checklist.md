# DDD: Modeling Order, Testing, and Review Checklist

Read before generating a new model, writing domain tests, or reviewing DDD code. Extends `.claude/rules/implementing-domain-driven-design.mini.md`.

## Modeling and testing

- When generating code, walk the model in order: context, language terms, tactical type, conservative Aggregate boundary, identity references, local invariants, Aggregate-oriented repositories, use-case services, and boundary translations.
- Test domain behavior and boundaries directly: Aggregate invariants, valid and invalid state transitions, Value Object validation, Domain Events as outcomes, repositories as infrastructure, translation layers, and application-service orchestration.

## Final checklist

- Is the Bounded Context explicit before interpreting names, modules, events, repositories, APIs, persistence, or integrations?
- Does the code use one local term per concept across tests, commands, events, repositories, services, and packages?
- Is Core Domain effort protected while supporting or CRUD areas stay simpler?
- Are context relationships, translation responsibilities, and upstream/downstream pressures visible?
- Are Aggregates small, root-protected, invariant-driven, identity-linked, and usually one per transaction?
- Are Entities behavior-bearing and Value Objects immutable, validated, and value-equal?
- Are Repositories Aggregate-root access points rather than generic DAOs or ORM leaks?
- Are Domain Events meaningful past-tense facts, and is Event Sourcing used only when event history is the right persistence model?
- Are Application Services coordinating use cases instead of owning domain decisions?
- Are client, foreign, persistence, and infrastructure representations kept outside the local domain model?
