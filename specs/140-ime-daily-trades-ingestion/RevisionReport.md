# Feature 140 Specification Revision Report

1. Revision outcome: COMPLETE

2. Files changed

- `UserStory.md`
- `Design.md`
- `Tasks.md`
- `SpecReview.md`
- `README.md`
- `RevisionReport.md` added

3. Findings addressed
   MAJOR-01: COMPLETE — `SourceFingerprint` is now explicitly an exact-observation identity, not a logical/business transaction ID. `ImeDailyTrades` is documented as an immutable provider-observation store, and canonical/revision semantics remain deferred.
   MAJOR-02: COMPLETE — requested-date integrity validation was added. Non-empty responses with mismatched or mixed source `date` values fail the import, preserve raw evidence, and cannot produce partial success.
   MAJOR-03: COMPLETE — bounded raw response evidence is now required for every IME HTTP response actually received, before typed parsing determines success/failure, including malformed outer JSON, malformed inner `d`, non-success responses with bodies, and date-integrity failures.
   MAJOR-04: COMPLETE — raw storage semantics are explicit. `RawResponseText` is preferred; repository-mandated `jsonb` is permitted only as a documented lossless JSON-value representation without byte-for-byte/order/whitespace guarantees.
   MINOR-01: COMPLETE — Admin execution now follows a deterministic rule: reuse an existing long-running/background pattern if present; otherwise execute one-day imports synchronously; no new queue solely for Feature 140.
   MINOR-02: COMPLETE — Admin future dates later than the current `Asia/Tehran` date are rejected before provider invocation unless repository evidence mandates an established alternative rule.
   MINOR-03: COMPLETE — the IME POST is documented as semantically read-only; bounded transient retries may use existing repository policy, while parse/contract/date-integrity/business-validation failures are not retried.
   MINOR-04: COMPLETE — explicit AC-to-task traceability matrix added to `Tasks.md`.

4. Acceptance criteria count

31

5. Task count

19

6. AC-to-task traceability status

COMPLETE — all AC-01 through AC-31 map to at least one implementation task; no orphan or extra AC references were found.

7. Scope boundary preserved: YES

8. Remaining ambiguities or repository-dependent decisions

- Concrete namespaces, project ownership, controller route/versioning, and authorization attributes.
- Existing Worker scheduler technology and its timezone configuration mechanism.
- Existing `TimeProvider`/clock/Persian-date abstraction.
- Existing Admin long-running-operation/background-job mechanism, if any.
- Existing HTTP resilience/retry registration and selected retryable 5xx policy.
- Existing raw-payload persistence convention and whether `RawResponseText` or a mandatory `jsonb` convention should be used.
- Exact numeric precision/scale and EF/PostgreSQL naming conventions.
- Existing conflict-safe insertion/bulk-insert helpers and import lifecycle entities.

These are repository-discovery items, not unresolved product-scope questions.

9. Files not changed and why

- `source-fields.json` — unchanged because the revision did not alter the observed 34-field IME source contract.

FEATURE_140_SPEC_REVISION_COMPLETE
