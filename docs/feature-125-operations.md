# Feature 125 operations

Feature 125 reads only the selected `Published` snapshot. AI requests never call CyclicalWaves,
run calculations, or expose raw provider payloads.

Feature 125 does not run a dedicated worker. It is executed through the existing worker scheduling
infrastructure as a downstream analytical pipeline:

```text
NadpcoScheduledSyncWorker schedule
    -> existing NADPCO ingestion workflow
    -> Feature 125 application orchestration
    -> calculation input/barrier and snapshot publication
    -> watch evaluation
```

The existing worker remains orchestration-only. Feature 125 calculations, P/S/P/E/equilibrium
normalization, IQR/outlier handling, ranking, publication, and watch transitions remain in their
existing application/domain/infrastructure services. No Feature 125 hosted service, scheduler,
deployment unit, or parallel lease mechanism is used.

## Configuration

The `IndustryRelativeValuation` section is validated at startup. `Enabled` defaults to `false`.
`DailyCadenceMinutes` is `1440..10080`; `SourceFreshnessHours` is `1..168` (default `26`);
`IqrMultiplier` is `1.5..5` (default `1.5`); result limits are `DefaultResultLimit 1..100`
(default `3`) and `MaximumResultLimit 1..1000` (default `100`), with default not above maximum.
Entry and exit streak thresholds are `1..30` (default `3`). Invalid values fail startup with an
options validation error; workers do not start with an invalid configuration.

Source-ingestion settings remain under `IndustryRelativeValuation:SourceIngestion`. Keep source
ingestion and calculation disabled until the Feature 125 migration and provider credentials are
ready. Apply the existing Financial ingestion migrations before enabling either worker.

The trigger is the existing `NadpcoScheduledSync:CadenceSeconds` schedule and its existing lease,
retry, timeout, and cancellation policy. `IndustryRelativeValuation:Enabled` gates the downstream
pipeline. When it is `false`, the existing worker completes its normal ingestion workflow and does
not execute Feature 125. When it is `true`, the completed ingestion run invokes Feature 125 with
the same correlation and cancellation context. The Feature 125 `DailyCadenceMinutes` setting
remains startup-validated feature configuration; it does not create a second scheduler.

## Deployment and recovery

1. Apply the Financial ingestion schema and verify the Feature 125 tables and indexes.
2. Verify provider health and run bounded source ingestion first.
3. Confirm fresh source facts and inspect barrier/readiness evidence.
4. Enable the daily calculation only after ingestion is healthy.
5. Enable AI reads after a `Published` snapshot exists.

For first enablement, use the following sequence:

1. Apply and verify the existing Financial ingestion migration.
2. Verify CyclicalWaves credentials and existing provider health.
3. Configure `IndustryRelativeValuation:SourceIngestion:Enabled` if a source refresh is required,
   enable `IndustryRelativeValuation:Enabled`, and allow the next existing worker schedule to run.
   The downstream pipeline refreshes source facts before calculating.
5. Verify the Feature 125 source barrier, calculation status, selected Published snapshot, and
   watch evaluation before enabling AI reads.

The first run uses only existing persisted provider facts and the current canonical membership.
It does not create synthetic historical snapshots or synthetic watch streaks.

The source lease is `IndustryRelativeValuationSourceIngestion`; calculation leases use the
`industry-relative-valuation:{industryId}:{calculationDate}` identity. Same-barrier retries are
idempotent. A corrected barrier creates an auditable version; a weaker or incomplete retry cannot
replace a published version. Provider failures, stale facts, and failed publication leave the last
published snapshot available and are logged with bounded identifiers.

The existing worker retry policy covers a downstream Feature 125 failure. The failure is logged
with the Feature 125 correlation ID, the current Published snapshot and watch state remain
authoritative, and no fallback or synthetic calculation is created. A later scheduled retry may
re-run ingestion and the downstream pipeline; established calculation version allocation,
PostgreSQL advisory locking, publication selection, and watch idempotency remain in force.

## Observability

Look for the `Feature 125` log events for source-run counts, lease contention, calculation status,
watch outcome/streaks, read latency, missing snapshots, and rejected members. Logs contain IDs and
counts only; raw provider payloads and credentials are not logged. A `Published` snapshot can still
contain unavailable member metrics. An `Inconclusive` calculation is diagnostic and is never read as
a financial result or allowed to advance a watch streak.

## Troubleshooting

- **Startup fails:** inspect the named `IndustryRelativeValuation` option and correct its range.
- **No result:** verify a selected `Published` row exists for the canonical industry.
- **Inconclusive:** inspect barrier completeness, source readiness, freshness, and benchmark clean
  counts; refresh the provider facts and rerun the date.
- **Provider unavailable/stale:** do not substitute another source. Keep the prior published version,
  repair ingestion, and rerun with a new barrier.
- **Repeated publication failure:** inspect the calculation status/failure evidence and database
  transaction logs; retry only after the failed dependency is repaired.
- **Slow ranking/read:** inspect returned member counts and limit. Ranking is persisted over the
  complete industry and limits are applied after rank computation; unusually large industries should
  be checked for query plans and index usage before increasing limits.

## Release checks

Run the Feature 125 unit filter, semantic/API filter, PostgreSQL integration tests when available,
and the impacted Features 114/115/118/119/120 suites. Record the migration/schema review and the
observed large-industry/read latency before enabling production workers. Confirm that the existing
worker is the only scheduling/deployment unit, that `IndustryRelativeValuation:Enabled` is set as
intended, and that one scheduled run produces a selected Published snapshot and at most one watch
evaluation for its calculation identity.
