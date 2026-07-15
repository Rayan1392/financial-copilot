# Tasks — AI Market Report and Personal Digest

## 1. Boundaries and Report Model

- [x] Reuse Feature 095 immutable pulse snapshots, Features 084/090/092 events, Feature 085 followed symbols, existing AI provider/orchestration, Feature 013 Billing, and Feature 097 delivery.
- [x] Make deterministic fact/evidence assembly precede AI rendering; the LLM must never calculate market facts, select unsupported causes, or alter evidence.
- [x] Define report scopes `PublicMarket`, `IntradayMarket`, and `PersonalDigest`, lifecycle `Pending`, `Generated`, `Fallback`, `Failed`, `Superseded`, and publication version/revision.
- [x] Define `MarketReport` with optional actor/tenant, trading date/window, report version, referenced snapshot/event ids, immutable evidence bundle/hash, narrative, caveats/confidence, generated/published timestamps, and model/prompt metadata.

## 2. Evidence and Narrative Policy

- [x] Build a deterministic evidence bundle containing pulse facts, comparison windows, selected drivers/anomalies/Codal events, followed-symbol events, units, freshness, confidence, and source citations.
- [x] Rank/select evidence deterministically with governed limits; persist excluded/partial/stale reasons so the narrative cannot imply complete coverage.
- [x] Version prompt template, rendering policy, evidence schema, model/provider, and safety policy; prohibit price targets, instructions, unsupported causality, and portfolio claims.
- [x] Require every numeric sentence to map to an evidence item and qualify causal language unless the source explicitly establishes causality.
- [x] Generate a deterministic fallback report from the same facts when LLM is unavailable, times out, violates validation, or produces unsupported claims.

## 3. Persistence and Version History

- [x] Persist reports and evidence immutably with unique generation idempotency key by scope/actor/trading date/window/evidence hash/policy version.
- [x] Create new report revision for intraday updates, corrected snapshots, or changed evidence; retain superseded versions and publication audit.
- [x] Index latest public report, actor latest digest, trading-date history, state, and publication time; define retention separately for evidence and transient model payloads.
- [x] Store no raw secret/provider credentials and minimize personal data in prompts, logs, and retained model metadata.

## 4. Use Cases and Scheduling

- [x] Implement build evidence, generate/validate/fallback, publish latest public report, get history/version, generate personal digest, and get actor latest digest use cases.
- [x] Enforce actor ownership, subscription capability, daily/manual generation limits, and Billing reservation/commit/release for metered generation.
- [x] Schedule intraday reports only after eligible Feature 095 snapshots and final reports after the final snapshot/source-settlement window; use Tehran trading calendar.
- [x] Make scheduled/manual runs idempotent with distributed lease, bounded concurrency, retry/backoff, poison handling, and cancellation/timeout.
- [x] For personal digest, snapshot followed-symbol membership/event set at generation time and never infer holdings, exposure, P/L, or suitability.
- [x] Publish notification intents only through Feature 097; report regeneration must not automatically duplicate delivery.

## 5. API and Telegram Contracts

- [x] Specify latest/history/version public report and actor-scoped digest endpoints with report status, revision, evidence/citations, confidence/caveats, freshness, and generated/published times.
- [x] Define Telegram report/digest commands, pagination/long-message splitting, source/open-web actions, and versioned callback ownership.
- [x] Clearly label partial intraday versus final report, fallback narrative, corrected revision, stale inputs, and unavailable personalized digest.

## 6. Observability and Tests

- [x] Trace pulse/event evidence through prompt, provider call, validator, Billing reservation, report revision, publication, notification, and alert history.
- [x] Measure evidence age/completeness, generation/validation/fallback rate, provider latency/tokens/cost, unsupported-claim rejection, publication lag, and delivery handoff.
- [x] Unit-test fact selection, comparison windows, evidence hashing, numeric-claim validation, caveats, fallback, report revision, and Persian rendering.
- [x] Integration-test actor isolation, entitlement/Billing rollback, schedule/idempotency, provider timeout/failure, corrected evidence revision, and Feature 097 handoff.
- [x] Given complete deterministic evidence, when generation succeeds, then all numeric claims map to persisted evidence and generated-at/confidence/caveats are shown.
- [x] Given LLM failure or invalid unsupported output, when generation runs, then a deterministic fallback is published without fabricated commentary and credits follow Billing failure policy.

## Completion Gate

- [x] Evidence validation, fallback, versioning, provider/Billing failure, schedule/idempotency, and notification handoff tests pass.
