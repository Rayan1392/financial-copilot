# Restore TSETMC instrument catalog coverage

Status: Approved by the owner on 2026-11-24 (Gate A and Gate B)

## Evidence

On 2026-11-24, production had 900 eligible companies. Only 26 had an active instrument with the matching ISIN. Of the other 874, 782 had an instrument record by code and 92 had no record by code. The latest industry valuation for 93 of 95 evaluated groups was inconclusive.

A read-only reconciliation by instrument code, ISIN, and provider validity found 853 additional exact active matches, 20 provider-inactive instruments, and one company absent from the returned flows. Flows 1, 2, 4, 5, and 7 responded. Flow 6 returned HTTP 403 in this run. Its coverage remains unverified. The result covers instrument identity. Publication also requires usable trades and valuation inputs.

## Proposed correction

Use the existing Instruments ingestion path for the API and scheduled worker. Include verified equity flows 1, 2, and 4 alongside the configured flows 5, 6, and 7. Run that same path across the catalog once after rollout. Preserve the canonical instrument ID and linked price history when an existing instrument is found.

Treat identical entries in multiple flows as a single observation. Flag conflicting identities or validity values for review. A failed flow must be reported and retried. It must neither prevent processing the successful flows nor be interpreted as evidence that an instrument is inactive. Keep the existing valuation publication criteria.

## Proposed implementation and verification

1. Update API and worker defaults, Compose, and the sample environment. Verify effective flow configuration and credentials in the target environment. Document the verified flow coverage in the TSETMC integration guide.
2. Update the existing instrument sync to reconcile flow results by instrument code, preserve existing instrument links, and surface partial coverage. Add focused tests for duplicate records, conflicts, failure of one flow, and repeat execution. Use the existing schema and sync entry points.
3. Compare the planned sync against a read-only baseline. Run the full sync after separately authorized rollout. Confirm eligible identity coverage, retained trade links, and publication for representative industries. Report the remaining unmatched, inactive, conflicting, and valuation-input cases with counts.
4. If the new sync causes a regression, restore the prior flow configuration and revert the application change through the release process. Capture pre-sync instrument-field values for any repair that requires a targeted data rollback.
