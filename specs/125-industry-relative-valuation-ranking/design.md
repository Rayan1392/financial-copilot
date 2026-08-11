# Feature 125 Design — Industry Relative Valuation Ranking

## Status

`DESIGN_APPROVED`

This is the remediated Stage 2 design. It defines an implementation boundary; it does not authorize
production code, migrations, `user-story.md`, or `tasks.md`.

## 1. Scope and invariants

Feature 125 compares companies only inside the canonical NADPCO industry. “Group” and “industry” are
aliases in user language, not separate taxonomies. The feature never compares raw P/E, P/S, or price
values between companies and never calculates on the AI request path.

For company `c`:

```text
PEPercent = CurrentPE / HistoricalAveragePE * 100
PSPercent = CurrentPS / HistoricalAveragePS * 100
EquilibriumPercent = CurrentMarketPrice / EquilibriumPrice * 100
```

The required CyclicalWaves mappings are:

| Metric | Current fact | Reference fact |
|---|---|---|
| P/E | P/E gauge `close` | P/E gauge `avg` |
| P/S | P/S gauge `close` | P/S gauge `avg` |
| Equilibrium | equilibrium gauge `close` (current market price) | equilibrium gauge `balance` |

All arithmetic uses `decimal`; non-finite, overflowed, missing, zero, and negative operands are
invalid. Invalid operands never enter a benchmark. A company-level invalid metric is displayed Red
with a data-quality reason, while a missing metric is neither Red nor Green.

## 2. Verified repository boundaries

The normalized catalog is `FinancialIngestionDbContext.Companies` joined by nullable
`Company.IndustryId` to `Industries.Id`. Industry identity is the row’s `ProviderName` plus
`ExternalId`; `Name` is display metadata only. Feature 125 uses the NADPCO provider scope and the
NADPCO `industryID`/`industryTitle` mapping from the catalog normalizer. `Company.Id` is the
canonical `CompanyId`; the NADPCO external company identifier and ISIN are source/linkage facts.
No query, grouping, or key uses the display title alone.

Feature 114 is the existing CyclicalWaves P/S synchronization path. It is leased, bounded,
idempotent, persists gauge and history rows, and does not call the provider on the AI path. Its
current visualization row contains `GaugeClose`, `BoundaryAverage`, `TtmPsRatio`, and
`ForwardPsRatio`; its contract explicitly states that circle `avg`, circle `close`, and TTM/Forward
values remain separate facts. `BoundaryAverage` is therefore not silently treated as the historical
P/S baseline.

Feature 071’s `PE_TTM` is a normalized financial metric and is not interchangeable with the P/E
gauge’s current/reference pair. Feature 125 owns a separate P/E gauge fact ingestion slice.

Features 118–120 remain the semantic, entity-resolution, and clarification authorities. Feature 114
owns P/S acquisition; Feature 125 consumes or extends that sync through one explicit fact projection,
not a duplicate P/S worker.

## 3. Industry universe

The industry membership set is the active NADPCO canonical catalog, not the intersection of metric
rows. A member is a company row with:

1. `Company.ProviderName` in the NADPCO canonical company scope;
2. a non-null `Company.IndustryId`;
3. an industry row whose `ProviderName` is the NADPCO scope;
4. active/current catalog status according to the existing company-catalog status convention.

The industry row is joined by `Company.IndustryId = Industry.Id`, and its `ExternalId` is the stable
NADPCO industry key. Missing classification is an `Unclassified` exclusion from industry views, not a
new industry. Inactive companies are retained in historical snapshots but excluded from new eligible
snapshots. Companies without any provider fact remain members with `InsufficientData/Unranked`.

Feature 114’s two-market `NoavaranEligibleCompanies` scope remains the P/S request scope. It does not
define Feature 125 membership. The daily calculator takes the union of canonical NADPCO members and
left-joins P/E, P/S, equilibrium, and price facts. Provider absence affects only metric readiness,
benchmark participation, and rank eligibility.

## 4. Provider-neutral fact contract

The application-facing contract is a provider-neutral `RelativeValuationSourceFact` with:

```text
CompanyId, ProviderName, SourceKind, SourceObservationId
CurrentValue, ReferenceValue
FetchedAtUtc, PersistedAtUtc, SourceWatermark, PayloadHash
Readiness, QualityCode, IdentityEvidence
```

`SourceKind` is `PEGauge`, `PSGauge`, `EquilibriumGauge`, or `MarketPrice`. A fact is usable only when
the source identity matches the requested canonical company/ISIN, both required operands are finite,
the payload passes bounds/shape validation, and the fact is not older than its configured freshness
threshold at calculation time.

Each accepted observation is immutable by source observation identity/hash. A changed provider value
creates a new fact version; it does not rewrite a previously used source fact. `SourceWatermark` is a
deterministic string containing provider, endpoint, canonical company, persisted observation id, and
payload hash. It is stored in every calculation input reference.

### 4.1 P/S contract and Feature 114 reuse

Feature 114 continues to fetch `ps/circle-chart-data/{symbolIsin}`, `ps-data/{symbolIsin}`, and
`ps/{isin}` through its existing authenticated CyclicalWaves client, response-size limit, retry,
identity, decimal, history, and raw-payload conventions. Its visualization rows remain unchanged.

The Feature 114 sync must additionally publish the provider-fact projection consumed by Feature 125:

```text
CurrentPS = circle-chart-data.close
HistoricalAveragePS = circle-chart-data.avg
```

This is one backward-compatible provider-fact projection owned by the existing P/S sync, not a second
ingestion path. `BoundaryAverage` remains visualization boundary data. The projection carries the
same `FetchedAtUtc`, `PayloadHash`, provider identity, and source observation id as the accepted gauge
payload. If `avg` is absent, non-finite, zero, or negative, the P/S relative fact is
`InvalidBaseline`; the visualization row may still follow Feature 114’s independent renderability
rules.

Required regression fixtures prove that `close` maps to current P/S and `avg` maps to historical
average P/S, and prove that `BoundaryAverage` is not used for the relative calculation.

### 4.2 P/E contract

Feature 125 adds a CyclicalWaves gauge request through the existing CyclicalWaves HTTP/authentication
pipeline:

```text
/api/pe/circle-chart-data/{isin}
```

The accepted JSON object contains `a,b,c,d,e,f,close,start,end,min,max,avg`. Unknown additive fields
are ignored. `close` is `CurrentPE`; `avg` is `HistoricalAveragePE`. The complete validated payload
is retained as a bounded raw-payload record and the normalized fact persists the two required values
plus payload hash, fetch/persist timestamps, endpoint, and provider identity.

404/204 is `NotFoundOrNoData`; malformed JSON, oversized body, non-finite/overflowed values,
identity mismatch, 401 after the existing auth retry, 429, timeout, network error, and 5xx are
distinct readiness/error codes. Request timeout, retry/backoff, concurrency, rate limiting, and
telemetry use the existing CyclicalWaves named policies/options; Feature 125 creates no parallel
authentication or resilience stack. Raw payloads are never logged in ordinary logs and symbols/ISINs
are bounded or hashed in telemetry labels.

### 4.3 Equilibrium contract

Feature 125 adds a request through the same pipeline:

```text
/api/equilibrium/gauge/{isin}
```

The accepted business fields are `enticker,ticker,per,lastcaldate,close,balance,maxbalance,minbalance,
volume,date,growth,a,b,c,d,e,f`. `close` is the required current market-price fact and `balance` is
the equilibrium-price fact. Other fields are retained for audit/future visualization but do not alter
the formula. The provider’s `ticker`/ISIN identity is checked against the requested canonical link.

The same bounded body, decimal, auth, timeout, retry, 429/5xx/network, no-data, malformed, identity,
telemetry, and raw-payload rules as P/S/P/E apply. `lastcaldate` and `date` are source metadata only;
the calculation uses the latest persisted observation and records `FetchedAtUtc`/`PersistedAtUtc`.
The product requirement does not require inventing a separate provider business date.

### 4.4 Market-price source decision

The equilibrium gauge `close` is the authoritative `CurrentMarketPrice` for this feature. Existing
market quote sources may be more authoritative for other product features, but substituting one here
would change the locked formula. If a future invariant requires a different source, it must be a
separately approved product/design revision; no silent source substitution is allowed.

## 5. Calculation date, readiness, and publication

`CalculationDate` is the Tehran business date obtained from the repository’s existing Tehran timezone
helper (`Asia/Tehran`, with the Windows fallback `Iran Standard Time`) at the calculation boundary.
The daily job runs once per configured business date; a rerun of the same date is a recalculation,
not a second day.

For each date, the calculator first captures a source barrier containing the selected fact version
and watermark for every canonical member and each source kind. Per-source freshness thresholds are
configuration-driven and evaluated against `PersistedAtUtc`; defaults are 26 hours for daily P/E,
P/S, equilibrium, and price facts. A source may be unavailable or stale without deleting membership.

Calculation status is durable and monotonic for a calculation version:

| Status | Meaning | AI read path |
|---|---|---|
| `Pending` | inputs are being assembled | not visible |
| `Ready` | all required source barriers and calculation rows exist | not visible |
| `Published` | complete, validated snapshot selected as current | visible |
| `Inconclusive` | history exists, but one or more required industry benchmarks cannot be evaluated | diagnostic/history only, never a valid watch day |
| `Failed` | calculation could not produce a consistent version | not visible as a financial result |

No partially updated provider generation is published as a complete result. A company can have
missing metrics in a `Published` industry snapshot, but the three watch benchmarks must each have at
least two clean observations and all required source barriers must be fresh for that industry to be a
valid watch evaluation. `CalculatedAtUtc`, optional `PublishedAtUtc`, per-metric readiness/reason,
source watermarks, and membership hash are persisted.

The calculation transaction atomically writes the versioned industry rows, member rows, rank rows,
and the watch evaluation/outbox event. Provider ingestion commits separately; the calculation never
reads uncommitted provider rows.

## 6. Deterministic benchmark algorithm

For each metric independently:

1. select canonical members and latest barrier-approved facts;
2. reject missing, stale, non-finite, zero, or negative operands;
3. normalize each valid company against its own reference;
4. sort normalized `decimal` values ascending;
5. compute quartiles using **R7 / linear interpolation** with zero-based index
   `h = (n - 1) * p`, `lower = floor(h)`, `upper = ceiling(h)`, and linear interpolation between
   the two decimal observations for `p = .25` and `.75`;
6. calculate `IQR = Q3 - Q1`, bounds `Q1 - 1.5*IQR` and `Q3 + 1.5*IQR`;
7. retain values inside the inclusive bounds; values outside are outliers;
8. publish the arithmetic mean only when at least two clean values remain.

R7 is deterministic for all sample sizes: for `[x1,x2]`, Q1=`x1`, Q3=`x2`; for
`[x1,x2,x3]`, Q1=`x1 + .5(x2-x1)`, Q3=`x2 + .5(x3-x2)`; for `[x1,x2,x3,x4]`, Q1=`x1 + .75(x2-x1)`,
Q3=`x3 + .25(x4-x3)`. For zero observations there is no benchmark; one clean observation is
insufficient. If `IQR == 0`, bounds equal the common quartile and only values equal to that bound are
clean; no value is an outlier merely because the interval has zero width. Decimal scale/rounding is
not applied during calculation; persisted values use the existing PostgreSQL fixed-precision decimal
convention. The algorithm identifier is `IQR-R7-1.5-v1`.

An outlier remains visible in the member result, with `IsOutlier=true` and the reason
`ExcludedFromIndustryBenchmark`; it is excluded only from that metric’s benchmark. Missing and
invalid reasons are distinct from statistical outlier reasons.

## 7. Classification and ranking

For a valid normalized metric, `Percent <= CleanIndustryAverage` is Green/Positive; `Percent >`
average is Red/Negative. Equality is Green. A metric whose own operands are zero/negative is Red with
an explicit `InvalidNonPositiveInput` reason. A metric without a publishable industry benchmark is
`Unclassifiable`, not Green or Red.

`PositiveMetricCount` is the absolute primary key. Any member with at least one classifiable metric is
rank-eligible, including `0/3`, `1/3`, and `1/2`. A `0/0` member remains in the full membership view,
has `InsufficientData/Unranked`, has no financial rank, and never consumes a Top-N slot.

The rank is a single total lexicographic order computed over the complete eligible industry before
Top-N or pagination:

1. `PositiveMetricCount` descending;
2. P/E percent ascending, with non-null before null;
3. P/S percent ascending, with non-null before null;
4. equilibrium percent ascending, with non-null before null;
5. `ValidMetricCount` descending;
6. canonical immutable `CompanyId` ascending.

Null ordering is global and fixed; it is not pairwise “skip missing.” Thus the comparator is
transitive and pagination-stable. `GlobalRank` is persisted. The API applies requested `TopN` only
after rank computation. `IndustryRanking:DefaultResultLimit` defaults to `3`, accepts `1..100`, and
`IndustryRanking:MaximumResultLimit` defaults to `100` and cannot exceed `1000`. Requests above the
maximum are rejected/clarified, not silently truncated.

## 8. Durable snapshot and correction model

The implementation adds versioned rows in the Financial ingestion persistence boundary (exact EF
names are implementation work, not a second taxonomy):

`IndustryRelativeValuationCalculation`: `Id`, `CalculationDate`, `IndustryId`,
`IndustryExternalId`, `IndustryTitleSnapshot`, `CalculationVersion`, `Status`,
`AlgorithmVersion`, `MembershipHash`, `SourceBarrierHash`, timestamps, and publication selection.

`IndustryRelativeValuationMetric`: one row per calculation/industry/metric with valid count, outlier
count, clean count, Q1/Q3, bounds, clean average, readiness, and reason.

`CompanyIndustryRelativeValuation`: one row per calculation/member with source observation ids/hashes,
raw current/reference values, normalized percentages, validity/outlier/classification/reason per
metric, positive/valid counts, rank, and rank version.

`IndustryWatchState` and append-only `IndustryWatchTransition` store current state, current and prior
streaks, last evaluated calculation id, transition date/reason, algorithm version, and event identity.

Unique keys are `(CalculationDate, IndustryId, CalculationVersion)` and
`(CalculationId, CompanyId)`. The selected current published version is the highest valid version
for the date, then highest calculation id, under an explicit unique selection marker. A retry with the
same barrier/hash is a no-op. A lower-readiness attempt cannot replace a published version. Corrected
provider data creates a new calculation version and leaves the old published rows auditable; the new
version is selected only after a complete atomic publish. Watch transitions reference the selected
calculation id, so same-date recalculation cannot advance a streak twice.

## 9. Long-term watch state machine

The durable states are `NotWatching`, `EntryPending`, `Watching`, and `ExitPending`. `Inconclusive`
is persisted as the daily evaluation outcome, not as a replacement durable status.

A valid watch day is a `Published` calculation in which all three clean averages exist and each has at
least two clean observations. Entry is true only when all three averages are `< 100`; exit is true
only when all three are `> 100`. Exactly `100` is neither predicate.

For a new valid day: in `NotWatching`, entry true increments the entry streak and enters
`EntryPending`; reaching `EntryConsecutiveSnapshots` (default 3) transitions to `Watching`. In
`Watching`, exit true increments the exit streak and enters `ExitPending`; reaching
`ExitConsecutiveSnapshots` (default 3) transitions to `NotWatching`. A valid day that satisfies
neither predicate clears both pending counters and returns to the stable state. Entry and exit counters
are mutually exclusive and never advance together.

For an inconclusive day, the durable state and counters are unchanged, an `Inconclusive` evaluation is
recorded, and the streak is paused rather than reset. A later valid day continues the prior pending
streak. Same-date evaluation is upserted by `(IndustryId, CalculationId, EvaluationKind)` and cannot
increment twice. Transition records include previous/next state, prior/new counters, calculation id,
reason, and algorithm version.

## 10. Semantic capabilities and read contracts

Feature 125 registers these versioned capabilities in the Feature 118 registry:

| Code | Required slots | Output | Route |
|---|---|---|---|
| `symbol_vs_industry_relative_valuation` v1 | `CompanyOrSymbol`; optional `Industry`, `ResultLimit` | persisted comparison | `IndustryRelativeValuationRead` |
| `industry_relative_valuation_ranking` v1 | `Industry`; optional `ResultLimit`, `Presentation` | ranked list | same |
| `industry_relative_valuation_summary` v1 | `Industry` or `CompanyOrSymbol` | summary | same |
| `symbol_pair_within_industry` v1 | two `CompanyOrSymbol` slots | pair comparison | same |

Aliases include Persian/English “industry/group,” ranking, relative valuation, and compare language;
metric meanings remain Feature 015/072-owned. Precedence is: explicit pair comparison, explicit
industry ranking, symbol-vs-industry language, then industry summary. Plain P/S remains the existing
metric lookup and explicit P/S gauge remains Feature 115.

The read executor accepts only canonical IDs, the selected published calculation id/date, and a
bounded limit. It returns industry identity, freshness, benchmark evidence, member rows, status,
rank/total eligible count, and data-quality/outlier explanations. It never calls CyclicalWaves and
never accepts an LLM formula, SQL, rank, average, or color.

Resolution uses Feature 119’s `Resolved`, `Ambiguous`, `NotFound`, and `Missing` outcomes. A symbol-only
request derives its industry from the canonical company row. An explicit industry resolves by NADPCO
`ExternalId` or normalized name/approved alias; same-title candidates across provider scopes are
ambiguous. Symbol-plus-industry mismatch returns `InvalidIndustryMembership`. Two symbols are both
resolved before comparing industries; different industries return `DifferentIndustries`, not a
cross-industry result. Feature 120 stores the pending industry/comparison slot and candidate ids for
one-turn clarification with optimistic versioning and replay idempotency.

## 11. Scheduling, leases, and operations

The existing Worker/DataSync patterns are reused: bounded hosted worker, configured cadence,
correlation id, distributed lease, cancellation/deadline, per-company failure isolation, retry policy,
and persisted activity/status. Feature 125 uses separate lease names for source ingestion and daily
calculation. A calculation lease key is `industry-relative-valuation:{CalculationDate}`; only one
worker may publish a date at a time. Operators can observe pending/ready/published/inconclusive/failed
counts, source barrier hashes, and failure codes through existing data-sync activity conventions.

Configuration keys and validation:

```text
IndustryRelativeValuation:Enabled                 bool
IndustryRelativeValuation:DailyCadenceMinutes    1440..10080
IndustryRelativeValuation:SourceFreshnessHours   1..168 (default 26)
IndustryRelativeValuation:IqrMultiplier           1.5..5 (default 1.5; locked initial algorithm)
IndustryRelativeValuation:DefaultResultLimit      1..100 (default 3)
IndustryRelativeValuation:MaximumResultLimit      1..1000 (default 100)
IndustryRelativeValuation:EntryConsecutiveSnapshots 1..30 (default 3)
IndustryRelativeValuation:ExitConsecutiveSnapshots  1..30 (default 3)
```

Startup options validation rejects invalid ranges and a default limit above the maximum. Algorithm
and rank versions are persisted, so configuration changes cannot silently rewrite history.

## 12. Required test/fixture coverage

The design is implementation-ready only with tests for: exact P/E/P/S/equilibrium mappings and
provider identity; 404/204, malformed, oversized, non-finite, zero/negative, timeout, 429, 5xx, and
auth outcomes; R7 samples 2/3/4, IQR zero, inclusive bounds, all missing/non-positive, one clean
value, and metric-specific outliers; missing versus invalid classification; 0/0, 0/3, 1/2 versus
1/3, complete ties, nullable total-order ranking, global rank before Top-N, and stable pagination;
canonical provider-scoped industry joins, inactive/missing classification, ambiguous titles,
wrong explicit industry, and cross-industry pairs; stale/partial barriers and all snapshot statuses;
same-date retry/concurrency, lower-readiness protection, corrected-version selection, catalog
movement; watch entry/exit, exact 100, inconclusive pause, mutual counters, and duplicate prevention;
and Feature 118–120 routing, clarification, follow-up, and LLM formula rejection.

## 13. Explicit non-goals

No production implementation, database migration, live provider call from AI, raw peer valuation
comparison, automated buy/sell recommendation, second industry taxonomy, duplicate P/S ingestion, or
user-story/task breakdown is part of this design gate.
