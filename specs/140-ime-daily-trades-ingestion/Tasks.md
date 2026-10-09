# Feature 140 — Implementation Tasks

## Execution rule

Before changing production code, inspect the repository and map every conceptual component in `Design.md` to existing conventions. Do not introduce duplicate scheduler, HTTP, repository, admin, clock, retry, raw-payload, or sync-lifecycle infrastructure when an equivalent already exists.

## Slice S1 — Repository discovery and contract fixture

### T140-01 — Audit existing architecture

Identify and document:

- Worker scheduling mechanism and timezone support;
- admin endpoint/authentication convention;
- HTTP client/resilience convention;
- ingestion/sync-run lifecycle models;
- raw JSON persistence convention;
- EF Core/PostgreSQL migration conventions;
- clock/Persian-date helpers;
- conflict-safe/idempotent insert conventions.

**Acceptance:** a short implementation note maps Feature 140 concepts to concrete repository types/paths before new architecture is introduced.

### T140-02 — Add representative IME response fixture

Create a test fixture from the supplied response covering normal and edge-shaped rows, including `xTalarReportPK = 0`, nullable fields, Persian text, decimals, and both null/non-null settlement data.

**Acceptance:** CI tests do not require the live IME service.

## Slice S2 — Provider client and date contract

### T140-03 — Add/reuse Persian date abstraction

Support strict `yyyy/MM/dd` Jalali validation, Iran-local current-date formatting, and future-date rejection for Admin requests.

**Acceptance:** valid/invalid date tests pass and no culture-dependent parsing is used.

### T140-04 — Add IME provider request/response contracts

Model the fixed request fields, outer `{ d }` envelope, and all 34 observed trade fields.

**Acceptance:** supplied fixture deserializes without dropping any known field.

### T140-05 — Implement typed IME HTTP client

Call `GetAmareMoamelatList`, pass both date fields as the requested Jalali date, capture the bounded raw HTTP response before typed parsing, perform two-stage deserialization, propagate cancellation, and use existing timeout/retry conventions. Treat this POST as semantically read-only for bounded transient retries; do not retry parse/contract/date-integrity failures.

**Acceptance:** tests distinguish successful empty response, non-2xx, malformed outer JSON, null/missing unusable `d`, and malformed inner JSON.

## Slice S3 — Persistence and idempotency

### T140-06 — Add import lifecycle persistence

Add/reuse an import/run entity capable of recording source, date, trigger, status, timestamps, counts, HTTP status, bounded raw response evidence (`RawResponseText` preferred, established `jsonb` allowed only with clarified JSON-value semantics), and bounded error diagnostics.

**Acceptance:** retries/replays create auditable run records without requiring one-run-per-date uniqueness.

### T140-07 — Add raw trade-row persistence

Add entity/configuration/migration for all known IME source fields plus ingestion metadata.

**Acceptance:** database schema preserves all known fields and does not contain analytical/company-mapping columns.

### T140-08 — Implement canonical source fingerprint

Use stable field ordering, explicit nulls, culture-invariant numeric formatting, and an established cryptographic checksum convention (prefer existing repository helper; otherwise SHA-256).

**Acceptance:** identical rows always produce identical fingerprints; a changed source field changes the fingerprint.

### T140-09 — Enforce database uniqueness

Add unique protection equivalent to `(Source, SourceFingerprint)` and appropriate lookup indexes.

**Acceptance:** identical concurrent/replayed rows cannot be duplicated even when `xTalarReportPK = 0`.

## Slice S4 — Shared importer

### T140-10 — Implement shared daily importer

Create one application service used by all triggers. It must create/track the import lifecycle, call the provider, retain raw response evidence before parsing determines success/failure, validate every returned row date against the requested target date, write conflict-safe observations, finalize counts/status, and expose a typed result.

**Acceptance:** no HTTP/persistence/import logic is duplicated in Worker or API trigger code.

### T140-11 — Implement failure and transaction semantics

Ensure provider, parse, cancellation, and persistence failures cannot be marked successful. Avoid holding a database transaction open across the network request if that conflicts with existing lifecycle patterns.

**Acceptance:** injected failures leave deterministic failed state and no false successful completion.

### T140-12 — Add structured telemetry

Emit bounded structured events and counts according to existing logging conventions.

**Acceptance:** start, provider outcome, parse failure when applicable, and final outcome are observable without routine full-payload logs.

## Slice S5 — Triggers

### T140-13 — Add Worker schedule

Schedule Saturday-Wednesday at 17:00 `Asia/Tehran`, using the repository's scheduler/timezone mechanism.

**Acceptance:** schedule tests prove correct included/excluded weekdays and independence from server-local timezone.

### T140-14 — Add authorized Admin API trigger

Add an endpoint following existing route/version/auth conventions that accepts a target Jalali date, rejects future dates before provider invocation, and invokes the shared importer. Reuse an existing long-running Admin job pattern if present; otherwise execute synchronously. Do not add a new queue solely for this feature.

**Acceptance:** authorized valid request returns import outcome; invalid date is rejected before provider call; unauthorized access follows existing policy.

## Slice S6 — Verification

### T140-15 — Provider/parser unit tests

Cover:

- exact fixed request parameters;
- two-stage deserialization;
- Persian strings;
- decimal values;
- empty `[]`;
- HTTP failure;
- malformed outer JSON;
- missing/unusable `d`;
- malformed inner JSON;
- cancellation.

### T140-16 — Persistence/idempotency integration tests

Cover:

- all known fields persisted;
- raw response persisted;
- first import inserts rows;
- identical replay inserts zero duplicates;
- concurrent same-date import remains duplicate-safe;
- zero `xTalarReportPK` rows remain distinct when their complete source rows differ;
- changed source row is preserved as a new observation and fingerprint is explicitly observation identity only;
- raw evidence is retained after malformed outer JSON, malformed inner `d`, and date-integrity failure;
- all response row dates match requested date -> success;
- mismatched/mixed response dates -> failure;
- future Admin date rejected before HTTP call;
- transient POST failures retry only according to existing policy; contract/parse/integrity failures do not retry;
- persistence failure does not report success.

### T140-17 — Trigger integration tests

Cover:

- Worker and Admin resolve/use the same importer;
- Admin authorization;
- historical date request;
- scheduler timezone/weekdays.

### T140-18 — Regression/build verification

Run focused tests plus relevant repository regression/build/migration checks. Record unrelated existing failures separately rather than weakening Feature 140 tests.

### T140-19 — Operational smoke checklist

After deployment, verify one known-date manual import, duplicate replay behavior, raw response retention, parsed count, and first scheduled execution date/time.

## AC-to-task traceability

| Task | Acceptance Criteria |
|---|---|
| T140-01 | AC-06, AC-18, AC-19, AC-20, AC-24, AC-27, AC-31 |
| T140-02 | AC-03, AC-04, AC-07, AC-08, AC-10, AC-11, AC-14, AC-17, AC-31 |
| T140-03 | AC-02, AC-20, AC-22, AC-23, AC-31 |
| T140-04 | AC-01, AC-03, AC-04, AC-07 |
| T140-05 | AC-01, AC-02, AC-03, AC-05, AC-08, AC-09, AC-10, AC-11, AC-26, AC-27, AC-31 |
| T140-06 | AC-05, AC-06, AC-09, AC-10, AC-11, AC-25, AC-28 |
| T140-07 | AC-04, AC-30 |
| T140-08 | AC-12, AC-14, AC-15, AC-16 |
| T140-09 | AC-12, AC-13, AC-14 |
| T140-10 | AC-05, AC-12, AC-17, AC-18, AC-25, AC-28 |
| T140-11 | AC-09, AC-10, AC-11, AC-17, AC-26, AC-28 |
| T140-12 | AC-29 |
| T140-13 | AC-19, AC-20 |
| T140-14 | AC-18, AC-21, AC-22, AC-23, AC-24, AC-25 |
| T140-15 | AC-01, AC-02, AC-03, AC-05, AC-07, AC-08, AC-09, AC-10, AC-11, AC-17, AC-22, AC-23, AC-26, AC-27 |
| T140-16 | AC-04, AC-05, AC-06, AC-12, AC-13, AC-14, AC-15, AC-16, AC-17, AC-28, AC-30 |
| T140-17 | AC-18, AC-19, AC-20, AC-21, AC-22, AC-23, AC-24, AC-25 |
| T140-18 | AC-31 |
| T140-19 | AC-05, AC-12, AC-18, AC-19, AC-20, AC-25 |

Every AC is mapped to at least one implementation task. Test-oriented tasks provide explicit verification coverage for the new revision requirements.

## Required implementation report

Create `ImplementationReport.md` in this feature folder with:

```text
1. Outcome
2. Repository architecture reused
3. Files changed
4. Migration/schema
5. Provider client and parsing behavior
6. Raw-response persistence
7. Source-fingerprint/idempotency strategy
8. Worker schedule and timezone evidence
9. Admin endpoint/auth evidence
10. Unit/integration/regression/build results
11. Operational smoke status
12. Remaining blockers
```

Final successful marker:

```text
FEATURE_140_IME_DAILY_TRADES_INGESTION_IMPLEMENTED
```
