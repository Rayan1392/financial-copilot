# Feature 126 — Final Acceptance Review

## Verdict

**APPROVED**

## Blocking findings

None.

## Acceptance and boundary verification

AC-01 through AC-21 are accepted. The latest remediation closes the previous AC-13/AC-15
blockers:

- Feature 125 consumes the exact admitted-universe handoff projection. The manifest snapshot is
  restricted to admitted companies, emits explicit `Missing` entries for absent metrics, excludes
  non-admitted facts, and uses a deterministic digest. The calculation input builder loads source
  facts only by manifest fact IDs, so non-admitted facts cannot affect calculation.
- Feature 125 validates the manifest and fencing token before downstream work and again at the
  side-effect boundary. The calculation writer locks and validates the lease row inside the same
  transaction that writes calculation/publication state and evaluates watch effects. The current
  owner succeeds; a takeover/stale owner is rejected and produces zero downstream effects.
- One accepted P/S acquisition produces both the Feature 126 `PSGauge` source fact and Feature 114
  visualization persistence, with no second provider fetch.
- Ownership remains correctly separated: Feature 126 owns scheduling, acquisition, source facts,
  lease/fencing, and handoff; Feature 125 owns calculation, publication, and watch behavior;
  Feature 114 owns visualization persistence and reads.

## Migration and tests

No Feature 126 migration, table, column, index, or run-history schema change is present.

Focused remediation verification passed:

- Unit tests: 94 passed.
- Integration tests: 17 passed.

The previously known unrelated full-suite authentication/provider and broad API/data-backed
failures remain outside this feature’s acceptance scope and introduce no blocking finding here.
