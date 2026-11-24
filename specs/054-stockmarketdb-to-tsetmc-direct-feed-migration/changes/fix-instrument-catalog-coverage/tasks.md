# Restore TSETMC instrument catalog coverage

Status: Approved by the owner on 2026-11-24 (Gate B)

- [x] Configure instrument flows 1, 2, 4, 5, 6, and 7 in the API, worker, and Compose.
- [x] Reconcile instrument responses by code without losing existing instrument IDs or trade references.
- [x] Expose failed flows and rejected observations. Verify duplicate, conflict, partial failure, and repeat-run behavior.
- [x] Run local checks and document the rollout and reconciliation procedure.
- [ ] After separate rollout authorization, run the existing production sync once and verify instrument coverage and representative industry comparisons.

The three instrument sync regression tests passed, including the repeat-run and trade-reference check on isolated PostgreSQL. All 24 architecture tests passed. Compose resolved the same six flows for the API and worker.

The broader filter reproduced six failures in `IndustryComparisonRoutingEndpointTests`. A clean checkout of the starting commit reproduced all six with the same assertion mismatch. The fixture returns the industry comparison table while the test expects the retired-source message.
