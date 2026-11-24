# Restore TSETMC instrument catalog coverage

Status: Approved by the owner on 2026-11-24 (Gate B)

- [x] Configure instrument flows 1, 2, 4, 5, 6, and 7 in the API, worker, and Compose.
- [x] Reconcile instrument responses by code without losing existing instrument IDs or trade references.
- [x] Expose failed flows and rejected observations. Verify duplicate, conflict, partial failure, and repeat-run behavior.
- [x] Run local checks and document the rollout and reconciliation procedure.
- [x] After separate rollout authorization, run the existing production sync once and verify instrument coverage and representative industry comparisons.

The three instrument sync regression tests passed, including the repeat-run and trade-reference check on isolated PostgreSQL. All 24 architecture tests passed. Compose resolved the same six flows for the API and worker.

The broader filter reproduced six failures in `IndustryComparisonRoutingEndpointTests`. A clean checkout of the starting commit reproduced all six with the same assertion mismatch. The fixture returns the industry comparison table while the test expects the retired-source message.

## Production verification

On 2026-11-24, the authorized [deployment](https://github.com/Rayan1392/financial-copilot/actions/runs/45390728992) completed successfully. The API and worker both loaded flows 5, 6, 7, 1, 2, and 4. The one-time Instruments sync completed at 15:51 Tehran and persisted 21,450 observations, with six successful flows and zero rejected observations. A snapshot comparison confirmed that all 12,327 pre-existing instrument IDs and codes remained present.

At 16:05 Tehran, 880 of 901 eligible companies had an active matching ISIN. TSETMC marked 20 inactive, and one had no catalog record by code. Fifty-six of 95 evaluated industries had a current published comparison, up from two before the release. The production valuation store contained current, complete rows for قهکمت, شگل, فولاد, and کگهر. The other 39 evaluated industries had insufficient valid observations for at least one required benchmark.
