# Feature 140 — Product and Architecture Self-Review

## Verdict

**READY_FOR_IMPLEMENTATION_AFTER_REPOSITORY_DISCOVERY_AND_REVIEW_GATE**

No product-scope blocker remains. Repository integration details must be validated before coding because the repo was not supplied.

## Major decisions reviewed

### 1. Raw payload plus typed raw rows

**Decision:** keep both bounded raw provider response evidence and typed rows. Prefer `RawResponseText`; reuse repository-mandated `jsonb` only as a documented lossless JSON-value representation, not a byte-for-byte copy.

**Reason:** raw payload protects against schema drift and parser mistakes; typed rows make future deterministic queries practical. Storing only JSON would make future analytics unnecessarily expensive; storing only typed columns would lose unknown future fields.

### 2. No company mapping in ingestion

**Decision:** IME `Symbol`, `ProducerName`, and `ArzehKonandeh` remain provider facts.

**Reason:** equating them with TSETMC/company identities without an explicit mapping feature would contaminate the raw layer and make later corrections difficult.

### 3. Fingerprint-based idempotency

**Decision:** fingerprint the full known source row and enforce database uniqueness. The fingerprint is an exact-observation identity only, not a logical transaction identifier.

**Reason:** observed `xTalarReportPK = 0` means that field cannot be the universal key. `arzehPk` may also participate in multiple observations/contracts and is not proven globally unique from the supplied evidence.

### 4. Preserve changed observations instead of upserting in place

**Decision:** a materially changed row gets a new fingerprint/new immutable observation. `ImeDailyTrades` is explicitly not a canonical/current-trade table.

**Reason:** provider correction semantics are undocumented. Raw ingestion should not invent which version is canonical. A later normalization/analytics feature can implement revision selection with evidence.

### 5. Explicit Iran timezone

**Decision:** schedule at 17:00 `Asia/Tehran`, never server-local time.

**Reason:** production hosts may run UTC or another timezone. Financial data acquisition must not depend on host configuration.


### 6. Requested-date integrity

**Decision:** every non-empty returned row must match the requested Persian date. Any mismatch or mixed-date payload fails the import, preserves raw evidence, and cannot be reported as a partial success.

### 7. Raw evidence on failure

**Decision:** whenever IME returns an HTTP response, bounded raw response evidence is retained before typed parsing. This includes malformed outer JSON, malformed inner `d`, non-success responses with bodies, and date-integrity failures.

### 8. Admin execution and date boundary

**Decision:** reject future Admin dates before provider invocation. Reuse an existing Admin long-running-operation/job pattern when available; otherwise execute one-day imports synchronously. No new queue is introduced solely for Feature 140.

### 9. POST retry semantics

**Decision:** the IME POST is semantically read-only for FinancialCopilot. Existing bounded transient retry policy may apply; deterministic parse/contract/integrity/business-validation failures are never retried.

## Risks and mitigations

| Risk | Severity | Mitigation |
|---|---:|---|
| IME endpoint contract changes | High | Preserve bounded raw response before parsing; tolerate unknown fields; fail on unsafe type/shape changes |
| Provider date-field naming is misleading | Medium | Treat observed Jalali behavior as boundary contract; isolate date formatting |
| Source lacks universal row ID | High | Full-row observation fingerprint + DB uniqueness; never treat fingerprint as logical transaction identity |
| Worker/Admin collide | Medium | DB uniqueness mandatory; optional existing distributed lock |
| Replays cause provider traffic | Low/Medium | Idempotent storage; optional existing per-date lock, no new lock subsystem required |
| Provider returns wrong/mixed date | High | fail integrity validation, retain raw evidence, do not persist successful partial batch |
| Numeric precision loss | High for future analytics | Persist numeric monetary/quantity fields as decimal/numeric, not double |
| Future analytics accidentally treat raw IME Symbol as stock symbol | High | Explicit naming/documentation; no company FK in Feature 140 |
| Full raw payload grows storage | Low initially | One response per import is acceptable; revisit retention/compression only with measured volume |
| Import marked successful after partial write | High | transactional finalization + failure tests |
| Thursday/Friday executions | Medium | scheduler-level weekday constraints + deterministic tests |

## Open repository-validation items

These are not product questions and should not block spec approval:

1. Which scheduler is currently used?
2. What admin route/version/auth policy should be reused?
3. Is there already a sync-run/raw payload model?
4. Is there a mandatory raw-payload convention (`RawResponseText` vs established `jsonb` JSON-value storage)?
5. What retry policy/helper is standard?
6. Is there an existing Persian-date utility/`TimeProvider` abstraction?
7. What bulk/conflict-safe insertion mechanism is standard?
8. What numeric precision/scale convention is used for financial fields?

## Scope check

The design deliberately excludes:

- forecasting;
- company identity mapping;
- Codal comparison;
- monthly aggregation;
- alerts;
- semantic routing;
- LLM tools;
- chat/UI behavior.

This keeps the feature small enough to implement and verify as infrastructure while preserving the strategic dataset needed for later product features.
