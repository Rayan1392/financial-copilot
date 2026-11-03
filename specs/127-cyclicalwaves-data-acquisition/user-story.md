# CyclicalWaves Data Acquisition Foundation

Status: Implementation planning only

## User story

As a Financial Copilot data platform,
I want to automatically acquire CyclicalWaves P/S gauge, Last P/S, P/E gauge, Last P/E, and Equilibrium data every day,
so that the system has a reliable, complete, historical, and replayable source of valuation data.

## Problem statement

Financial Copilot does not yet have one simple acquisition boundary that reliably captures all
five required CyclicalWaves valuation responses for every company exposed by
`NoavaranEligibleCompanies`. Provider responses can change between daily checks, and a provider or
worker failure must not erase previously acquired evidence or force the complete cycle to restart
unsafely.

The platform must preserve complete provider responses rather than selected values such as
`close`, `avg`, or `balance`. It must also distinguish a changed provider response from the
operational fact that the provider was checked and returned unchanged data or a failure.

The provider's date fields are not a reliable acquisition timestamp. The platform must record UTC
request times based on when it actually calls the provider. If the worker stops, persisted progress
must allow a later execution to continue without losing committed data or inserting duplicate
snapshots.

## Business value

- Establishes one authoritative acquisition source for CyclicalWaves P/S gauge, Last P/S, P/E gauge,
  Last P/E, and equilibrium data.
- Preserves full provider evidence for audit, debugging, replay, and future projections.
- Builds immutable response history only when provider data changes, avoiding redundant snapshots.
- Proves daily provider coverage through separate acquisition-check history, including failures and
  unchanged responses.
- Protects provider stability through sequential calls, bounded retries, timeouts, and pacing.
- Keeps previous valid data available during provider incidents.
- Creates a durable database boundary that future consumers can use without calling CyclicalWaves
  directly.

## Scope

- A configuration-controlled daily UTC worker, disabled by default.
- Startup continuation for an incomplete current UTC acquisition cycle.
- Deterministic company traversal using the existing `NoavaranEligibleCompanies` view.
- Deterministic per-company request order: P/S gauge, Last P/S, P/E gauge, Last P/E, then equilibrium.
- Sequential provider communication with at most one request in flight.
- Integration with the five approved CyclicalWaves endpoints.
- Complete successful raw JSON response preservation in immutable snapshots.
- Canonical JSON hashing, latest-response comparison, and changed/no-change detection.
- Separate acquisition-check records for `Changed`, `NoChange`, and `Failed` results.
- Atomic snapshot and successful-check persistence.
- Restart-safe continuation based on successful daily acquisition checks.
- Bounded timeout, retry, request pacing, failure isolation, and sanitized diagnostics.
- EF Core entities, constraints, indexes, and an additive PostgreSQL migration.
- Unit, provider-contract, PostgreSQL integration, recovery, and worker behavior tests.

## Out of scope

- Feature114 visualization implementation or migration in this feature.
- Gauge rendering, visualization behavior, or frontend changes.
- Valuation calculations, derived metrics, ranking, publication, or watch behavior.
- Feature125 integration.
- Feature126 architecture or integration.
- Leases, fencing, distributed locks, handoff, heartbeats, or multiple-worker coordination.
- Parallel provider requests, batching, speculative prefetch, or company fan-out.
- RabbitMQ, outbox messages, multi-stage orchestration, or manual acquisition APIs.
- Company catalog or eligibility-view creation, mutation, or deletion, and any eligibility filter
  beyond the existing `NoavaranEligibleCompanies` view.
- Historical missed-day reconstruction.
- Consumer-specific structured columns in acquisition tables.
- Read-model or projection implementation for future consumers.

## Functional requirements

### Acquisition

1. When enabled, the worker must evaluate the current UTC cycle at startup and then run on the
   configured five-field UTC cron schedule.
2. The worker must load every row from `NoavaranEligibleCompanies`, projecting
   `ExternalCompanyId`, `CompanySymbol`, and `SymbolIsin`, and must not apply another downstream
   eligibility, ranking, or visualization filter. The view row's `Id` may also be retained solely
   for the existing `Companies.Id` persistence foreign key.
3. For each company with a valid symbol ISIN, the worker must call the P/S gauge, Last P/S, P/E gauge,
   Last P/E, and equilibrium endpoints in that exact order.
4. Provider calls must be awaited sequentially. At most one logical or physical provider request,
   including retries, may be in flight.
5. A missing or invalid `SymbolIsin` in an eligible-company view row must create explicit failed
   checks for all five metrics without silently reducing expected coverage.
6. P/S acquisition is mandatory even while Feature114 independently fetches P/S for its current
   behavior.

### Provider response handling

7. The acquisition client must retain the exact successful JSON response text and the transport
   metadata needed for persistence and diagnostics.
8. P/S and P/E responses must satisfy the approved gauge contract. Last P/S and Last P/E responses must contain
   the required `data.symbol`, `data.ticker`, `data.ps_ratio`, `data.close`, and `data.date` fields.
   Equilibrium responses must satisfy their contract and, when `enticker` is present, match the
   requested normalized ISIN.
9. Unknown additive JSON properties must be accepted and preserved.
10. Malformed, truncated, non-object, contract-invalid, or identity-mismatched responses must be
    recorded as failures and must not replace a previous valid snapshot.
11. `RequestedAtUtc` and `AcquisitionDateUtc` must come from the platform's UTC `TimeProvider` at
    the actual HTTP attempt boundaries, not from provider fields such as `date` or `lastcaldate`.

### Persistence and source of truth

12. `CyclicalWavesMetricSnapshots` must store complete accepted response versions for each company
    and metric.
13. `RawResponseJson` must be stored as text and is the canonical source of truth.
14. Acquisition storage must not contain consumer-specific parsed fields. A future structured
    consumer must use a separate, rebuildable projection or read model linked to the source
    snapshot.
15. The complete JSON document must be canonicalized deterministically and hashed with SHA-256
    without omitting provider fields.
16. A response whose canonical hash differs from the latest snapshot must create a new immutable
    snapshot linked to its predecessor.
17. A response whose canonical hash equals the latest snapshot must not create another snapshot.
18. Legitimate response reversions such as `A -> B -> A` must preserve all three transitions.
19. `CyclicalWavesAcquisitionChecks` must separately record each completed logical check as
    `Changed`, `NoChange`, or `Failed`.
20. A changed/no-change check must link to its accepted/current snapshot. A failed check must not
    link to a snapshot or invalidate existing data.
21. A changed snapshot and its successful acquisition check must commit in one transaction.

### Recovery and continuation

22. A successful acquisition check for the current UTC cycle, company, and metric must act as a
    completion checkpoint.
23. On restart, completed current-cycle metrics must be skipped without another provider call.
24. A metric with no successful current-cycle check, including one with earlier failed checks, must
    remain eligible for retry.
25. If a transaction does not commit, the next execution must safely re-acquire that metric.
26. If a transaction commits before shutdown, the snapshot and check must remain available and
    must not be duplicated on restart.

### Provider protection and failure isolation

27. One HTTP resilience pipeline must own all retries; nested retry layers are prohibited.
28. `RetryCount` must bound physical attempts, and `TimeoutSeconds` must apply per physical attempt.
29. Transient network failures, timeouts, HTTP `408`, `429`, and `5xx` responses must use bounded
    exponential backoff with jitter; a valid bounded `Retry-After` must be honored for `429`.
30. Non-retryable responses and exhausted transient failures must create stable, sanitized failure
    outcomes when persistence is available.
31. The configured request delay must be applied once between logical provider operations and must
    be cancellation-aware.
32. A P/S failure must not prevent P/E or equilibrium acquisition. A company failure must not stop
    later companies.
33. Previous valid snapshots must remain immutable and available after all provider failures.

### Future consumption boundary

34. This feature owns provider communication, raw response preservation, snapshot history,
    acquisition checks, canonical hashing, change detection, and duplicate prevention.
35. This feature does not own gauge rendering, visualization, frontend behavior, valuation
    calculation, or downstream projections.
36. A separate future change must migrate Feature114 to read stored P/S data from
    `CyclicalWavesMetricSnapshots` through an appropriate database-backed read model.
37. After that future migration, Feature114 must not call CyclicalWaves provider endpoints
    directly.

## Acceptance criteria

### Data acquisition

1. Given the feature is enabled and a daily cycle begins, when a company has a valid ISIN, then the
   worker attempts all four provider datasets: P/S gauge, Last P/S, P/E, and equilibrium.
2. Given eligible and ineligible company rows exist in `Companies`, when the company source is
   queried and the cycle executes, then only rows exposed by `NoavaranEligibleCompanies` are
   acquired in stable normalized-ISIN and `CompanyId` order, using the view's `SymbolIsin` without
   an `EnTicker` fallback.
3. Given one company, when its metrics execute, then the logical order is exactly
   `PS -> LastPS -> PE -> Equilibrium`.
4. Given any acquisition cycle, when provider requests are observed, then no more than one logical
   or physical provider request is in flight at any time.
5. Given an accepted provider response, when it is persisted, then the complete response is
   available in `RawResponseJson`; no selected-value-only representation replaces it.
6. Given unknown additive JSON properties in a valid response, when acquisition completes, then
   those properties are retained in `RawResponseJson`.
7. Given Feature114 still uses its current provider-backed behavior, when this worker runs, then it
   still independently acquires and persists P/S data.

### Persistence

8. Given no prior snapshot, when a valid response is accepted, then one immutable snapshot and one
   `Changed` acquisition check are committed.
9. Given a latest snapshot with a different canonical hash, when a valid response is accepted, then
   one successor snapshot and one `Changed` check are committed.
10. Given a latest snapshot with the same canonical hash, when the provider is checked again, then
    no duplicate snapshot is inserted and one `NoChange` check is stored.
11. Given a changed or unchanged valid response, when persistence succeeds, then the acquisition
    check is stored separately from the snapshot and links to the accepted/current snapshot.
12. Given a provider failure, when the failure reaches a terminal result, then a `Failed` check is
    stored when the database is available and no snapshot is inserted or invalidated.
13. Given previous valid data and a later provider failure, when consumers inspect acquisition
    history, then the previous snapshot remains available and unchanged.
14. Given a successful response, when its timestamps are inspected, then acquisition/request times
    reflect the platform's actual UTC HTTP attempt times and are not inferred from provider dates.
15. Given semantically equivalent JSON with different property order, whitespace, or numeric lexical
    form, when it is hashed, then the canonical response hash is unchanged.
16. Given response history `A -> B -> A`, when all three responses are acquired on different
    changes, then three linked snapshots preserve the complete transition history.
17. Given a future consumer needs structured values, when that consumer is designed, then it uses a
    separate read model or projection; the acquisition storage model and canonical
    `RawResponseJson` evidence remain unchanged.

### Recovery

18. Given a snapshot and successful check committed before worker shutdown, when the worker
    restarts in the same UTC cycle, then the committed metric is retained and is not called or
    inserted again.
19. Given shutdown before the metric transaction commits, when the worker restarts, then the metric
    remains incomplete and can be safely re-acquired.
20. Given an interrupted multi-company cycle, when startup recovery runs, then it skips successful
    current-cycle company/metric checks and continues unfinished work.
21. Given one or more failed checks and no successful check for the same cycle/company/metric, when
    the worker executes again, then that metric can be retried.
22. Given replay of an already persisted response, when checkpoint protection is bypassed or stale,
    then latest-hash comparison still prevents a duplicate snapshot.

### Provider protection

23. Given normal execution, when provider traffic is measured, then there are no parallel provider
    calls, batches, or per-company fan-out.
24. Given a transient failure, when retry handling executes, then total physical attempts do not
    exceed `1 + RetryCount` and no second retry layer is invoked.
25. Given consecutive logical provider operations, when the next operation begins, then the
    cancellation-aware `RequestDelayMilliseconds` pacing has been observed.
26. Given a P/S endpoint failure for one company, when the failure is recorded, then the worker
    continues with that company's P/E and equilibrium operations.
27. Given any metric or company failure, when the cycle continues, then later companies remain
    eligible and the hosted worker remains running.

### Future boundary

28. This feature owns CyclicalWaves P/S gauge, Last P/S, P/E, and equilibrium acquisition, raw response
    preservation, snapshot/check history, and duplicate detection only; it performs no rendering,
    visualization, frontend, ranking, or valuation calculation work.
29. The system acquires P/S data even before Feature114 migration is completed. Feature114
    migration to database-backed consumption is a separate future change.
30. After that separate migration, Feature114 reads stored P/S data from the database and does not
    call CyclicalWaves provider endpoints directly.
