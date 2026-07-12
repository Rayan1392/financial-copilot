# Tasks — Personal Market Radar

## 1. Discovery and Compatibility

- [ ] Read `specs/README.md`, `specs/implementation-checklist.md`, this story, and all declared dependencies.
- [ ] Inspect current code and uncommitted changes before implementation.
- [ ] Reuse existing Identity, Billing, AI orchestration, provider ingestion, company resolution, and insight-event boundaries.
- [ ] Record any conflict with an implemented spec before changing code.

## 2. Domain and Contracts

- [ ] Define governed entities, enums, commands, queries, DTOs, repository ports, and policy interfaces required by this story.
- [ ] Reuse canonical `ExternalCompanyId` for symbol/company references.
- [ ] Version detector, rule, filter, or rendering definitions where behavior affects historical explainability.

## 3. Persistence and Data Integrity

- [ ] Add additive EF Core migration only when persistence is required.
- [ ] Add uniqueness/idempotency constraints for actor/entity/rule/delivery keys.
- [ ] Add indexes for actor, company, status, event time, and expiry queries.
- [ ] Preserve immutable evidence/provenance snapshots for anything sent to the user.

## 4. Application Use Cases

- [ ] Implement the primary use cases for Personal Market Radar.
- [ ] Validate actor ownership and entitlement before execution.
- [ ] Return explicit Missing/Unavailable/Stale states instead of fabricated fallback values.
- [ ] Keep rule evaluation and numeric calculations deterministic and unit-testable.

## 5. API / Telegram Integration

- [ ] Add or extend protected backend endpoints using the project versioning conventions.
- [ ] Add Telegram commands, callback actions, or deep links only through the Telegram adapter.
- [ ] Render Persian messages within Telegram length limits and split safely when necessary.
- [ ] Preserve correlation ids across Telegram update, application use case, AI workflow, Billing, and notification delivery.

## 6. Billing, Entitlements, and Security

- [ ] Map access to existing plan capabilities and entitlements.
- [ ] Reserve/finalize credits only for metered AI operations; deterministic notifications must follow product policy.
- [ ] Enforce replay protection and idempotency on Telegram updates, callbacks, payment callbacks, and writes.
- [ ] Redact sensitive payload fields from logs and telemetry.

## 7. Observability and Operations

- [ ] Add structured telemetry for received, evaluated, triggered, suppressed, delivered, failed, and retried operations as applicable.
- [ ] Add health diagnostics for Telegram transport and dependent services.
- [ ] Add admin visibility for failures and dead-letter items without exposing user message content unnecessarily.
- [ ] Define retention and cleanup policies for transient delivery data.

## 8. Tests and Completion Gate

- [ ] Unit-test validation, rule/policy logic, deduplication, and deterministic rendering.
- [ ] Integration-test actor isolation, authorization, entitlement, persistence constraints, and endpoint contracts.
- [ ] Regression-test that existing web/API behavior remains unchanged.
- [ ] Regression-test that no duplicate credit charge, alert, checkout fulfillment, or event is produced during retries.
- [ ] Verify no buy/sell recommendation wording and no unsupported causal claims.
- [ ] Update `implementation-checklist.md` to `[x]` only after build, tests, migration verification, and completion evidence pass.

## Implementation Constraints

- Do not introduce a second AI orchestration path for Telegram.
- Do not access financial-provider databases directly from Telegram handlers.
- Do not create Telegram-specific credits, balances, subscriptions, or usage ledgers outside Feature 013.
- Do not present detections as guaranteed signals or investment advice.
- Preserve deterministic evidence, source provenance, freshness, and confidence values.
- Keep all write operations idempotent and actor-scoped.
