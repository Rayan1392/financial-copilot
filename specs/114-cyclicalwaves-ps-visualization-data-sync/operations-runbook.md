# P/S visualization sync operations runbook

1. Rotate the bearer token previously disclosed in the feature request. Configure CyclicalWaves
   credentials only through the approved secret/environment mechanism; never add a token, browser
   header, or raw response to source control.
2. Apply the Financial Ingestion migration, set `CyclicalWavesPsSync:Enabled` only after a successful
   small DataAdmin dry run, and keep the default request/response limits unless provider capacity is
   approved.
3. Use `POST /api/v1/admin/cyclicalwaves/ps-visualization/scope/dry-run` first. Investigate all
   `MissingOrInvalidIsin`, `CompanyMappedToMultipleIsins`, and `IsinMappedToMultipleCompanies`
   results; conflicting identities are never guessed.
4. Start bounded backfill with `POST .../sync` and `maxCompanies`. Inspect the correlation response,
   then the company read endpoint. Retry a company or one dataset only with the snapshot/history
   endpoints.
5. On history conflict, metadata mismatch, stale data, provider rate limit, or authentication failure,
   preserve the existing active series and investigate the bounded status/warning code. Do not delete
   historical rows to recover.
6. Before production enablement, verify the persisted sample-company projection against the approved
   شراز and غگلپا captures: equal 30-degree arcs in `a..f` visual order, `min..max` axis, TTM
   `ps_ratio` needle, and edge clamping. Keep rollout disabled if parity regresses.
