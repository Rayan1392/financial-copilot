# Features 117–123 Implementation Evidence

Recorded: 2026-08-08

## Scope verified

- All enabled deterministic and agent capabilities enter through the validated semantic frame and dispatcher on V1, native V2, and V2 fallback paths.
- Monthly trend, direct metric lookup, product mix, financial statements, disclosures, sales quality, P/S gauge, scanner, and Comprehensive Analysis use semantic executors on active production paths.
- The execution coordinator owns the single semantic execution decision; the facade billing hook remains the only reservation/finalization boundary.
- Comprehensive Analysis receives canonical symbol/topic/date/limit slots and no longer invokes its raw-text parser inside the semantic executor.
- Suggested actions are additive API/persistence contracts and render consistently for live/reloaded Web responses and Telegram numbered actions.
- Semantic feedback, aggregate dashboard queries, alert evaluation, offline regression, reviewed phrase promotion, canary activation, and immediate rollback are implemented.

## Verification evidence

| Check | Result |
|---|---|
| V2 route integration suite | 54/54 passed |
| Architecture suite | 10/10 passed, including active-path and documentation-status guards |
| Full unit suite | 1379/1380 passed; only `CyclicalWavesAuthHandlerTests.Response401_TriggersReloginAndRetry` failed outside Features 117–123 |
| Full integration suite | 393/411 passed; remaining failures are legacy scanner fixtures/policy expectations, financial-statement schema metadata, and market-insight fixtures; migrated V2 suite is fully green |
| Suggested-action Web tests | 5/5 passed |
| Frontend production build | Passed |
| Full frontend tests | 33/34 passed; only the unrelated disclosure receipt-timezone assertion failed |

## Production evidence gate

The code and CI-reproducible controls for Task 123.9 are implemented, but Features 122 and 123 must remain in progress until the following real deployment evidence is attached:

- CI run identifier for the release candidate.
- Dashboard query or snapshot covering capability, registry version, channel, outcome, bounded reason, language mismatch, wrong route, and failure rate.
- At least 24 hours of per-capability canary observation.
- Measured language mismatch, wrong-route, and failure rates within `SemanticCompletionEvidencePolicy` thresholds.
- Confirmation that configuration-driven rollback was exercised or validated for the deployed version.

No synthetic production observation is recorded in this repository. Task 122.10 remains `[~]` until this gate passes; physical removal of rollback-only legacy rules must happen after that observation, not before it.

## Documentation consistency

`CleanArchitectureDependencyTests.SemanticFeatureDocumentation_TracksVerifiedImplementationState` fails when Features 117–123 drift back to stale status, when a verified task is unchecked, when Task 122.10 is incorrectly reported complete before production observation, or when this evidence record is removed.
