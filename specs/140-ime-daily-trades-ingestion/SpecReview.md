# Feature 140 — Product and Architecture Self-Review

## Verdict

**READY_FOR_IMPLEMENTATION_AFTER_REPOSITORY_DISCOVERY**

No product-scope blocker remains. Repository integration details must be validated before coding because the repo was not supplied.

## Major decisions reviewed

### 1. Raw payload plus typed raw rows

**Decision:** keep both the original provider response and typed rows.

**Reason:** raw payload protects against schema drift and parser mistakes; typed rows make future deterministic queries practical. Storing only JSON would make future analytics unnecessarily expensive; storing only typed columns would lose unknown future fields.

### 2. No company mapping in ingestion

**Decision:** IME `Symbol`, `ProducerName`, and `ArzehKonandeh` remain provider facts.

**Reason:** equating them with TSETMC/company identities without an explicit mapping feature would contaminate the raw layer and make later corrections difficult.

### 3. Fingerprint-based idempotency

**Decision:** fingerprint the full known source row and enforce database uniqueness.

**Reason:** observed `xTalarReportPK = 0` means that field cannot be the universal key. `arzehPk` may also participate in multiple observations/contracts and is not proven globally unique from the supplied evidence.

### 4. Preserve changed observations instead of upserting in place

**Decision:** a materially changed row gets a new fingerprint/new raw observation.

**Reason:** provider correction semantics are undocumented. Raw ingestion should not invent which version is canonical. A later normalization/analytics feature can implement revision selection with evidence.

### 5. Explicit Iran timezone

**Decision:** schedule at 17:00 `Asia/Tehran`, never server-local time.

**Reason:** production hosts may run UTC or another timezone. Financial data acquisition must not depend on host configuration.

## Risks and mitigations

| Risk | Severity | Mitigation |
|---|---:|---|
| IME endpoint contract changes | High | Preserve raw response; tolerate unknown fields; fail on unsafe type/shape changes |
| Provider date-field naming is misleading | Medium | Treat observed Jalali behavior as boundary contract; isolate date formatting |
| Source lacks universal row ID | High | Full-row canonical fingerprint + DB uniqueness |
| Worker/Admin collide | Medium | DB uniqueness mandatory; optional existing distributed lock |
| Replays cause provider traffic | Low/Medium | Idempotent storage; optional existing per-date lock, no new lock subsystem required |
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
4. Is `jsonb` an established convention?
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
