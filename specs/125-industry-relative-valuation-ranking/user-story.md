# Feature 125 — User Story and Acceptance Criteria

## Status

`USER_STORY_READY_FOR_REVIEW`

This document translates the approved Stage 2 design into deterministic
product and system acceptance criteria. It does not authorize production
code, migrations, or task breakdown.

## Product Goal

Allow a user to understand a company’s valuation relative to companies in its
canonical NADPCO industry. Each company is normalized against its own
historical or equilibrium reference. Raw P/E, P/S, and price values are never
compared between companies, and the AI request path never calculates or
fetches provider data.

## Primary User Story

As an investor researching an Iranian listed company, I want to compare its
normalized P/E, P/S, and equilibrium position with its canonical NADPCO
industry, so that I can understand relative valuation context without an
unsupported buy/sell signal.

## User Value

Successful results identify the industry and calculation, show normalized
percentages and clean benchmarks, expose classification, quality and outlier
reasons, and provide deterministic rank and provenance evidence. Missing data,
invalid inputs, incomplete benchmarks, and corrections remain visible in their
appropriate result or diagnostic/history contract.

## Supported User Intents

Feature 118 capabilities are:

- `symbol_vs_industry_relative_valuation` v1;
- `industry_relative_valuation_ranking` v1;
- `industry_relative_valuation_summary` v1;
- `symbol_pair_within_industry` v1.

Industry and group are aliases for the same canonical NADPCO industry. Plain
P/S metric lookup remains its existing owner, and an explicit P/S gauge request
remains Feature 115.

## Scope

### In Scope

- Canonical NADPCO membership and provider-scoped industry resolution.
- Persisted P/E, P/S, equilibrium, and equilibrium-gauge market-price facts.
- Per-company normalization, independent benchmarks, classification, ranking,
  daily snapshots, freshness, publication, audit, and correction evidence.
- Feature 118 routing with Feature 119 resolution and Feature 120 clarification.
- Long-term watch state and persisted transitions from valid published snapshots.
- Provider, calculation, configuration, lease, and operational evidence defined
  by the approved design.

### Out of Scope

- Raw company-to-company P/E, P/S, or price comparison.
- AI-side financial/statistical calculation, SQL generation, or provider calls.
- Automated buy/sell recommendations.
- A second taxonomy or grouping by display title alone.
- A duplicate P/S ingestion worker or changes to Feature 114 visualization
  semantics.
- Production code, migrations, or `tasks.md` in this stage.

## Locked Business Rules

| Metric | Current fact | Reference fact | Normalized value |
|---|---|---|---|
| P/E | P/E gauge `close` | P/E gauge `avg` | `CurrentPE / HistoricalAveragePE * 100` |
| P/S | P/S gauge `close` | P/S gauge `avg` | `CurrentPS / HistoricalAveragePS * 100` |
| Equilibrium | equilibrium gauge `close` | equilibrium gauge `balance` | `CurrentMarketPrice / EquilibriumPrice * 100` |

All arithmetic uses decimal values. Missing, non-finite, overflowed, zero, or
negative operands cannot produce a valid normalized metric. Missing is neither
Green nor Red. Zero/negative is Red with `InvalidNonPositiveInput` (or the
approved equivalent) and cannot enter a benchmark.

## Industry and Data Rules

- Membership is the active NADPCO canonical catalog: canonical company scope,
  non-null `Company.IndustryId`, provider-scoped industry row, and active/current
  catalog status.
- Industry identity is provider scope plus stable NADPCO `ExternalId`; display
  title is metadata only. A missing `IndustryId` is `Unclassified` and excluded
  from industry views.
- Inactive companies remain in historical snapshots but are excluded from new
  eligible snapshots. A company moving industry affects future snapshots only.
- Companies without provider facts remain members and may be `0/0`
  `InsufficientData/Unranked`.
- Missing, invalid, stale, unavailable, malformed, identity-mismatched, and
  outlier reasons remain distinct. Invalid facts never enter a benchmark.
- An outlier remains visible and is excluded only from that metric’s benchmark,
  with `IsOutlier=true` and `ExcludedFromIndustryBenchmark`.

## Benchmark, Classification, and Ranking Rules

For each metric independently, valid normalized decimal values are sorted and
R7 quartiles are calculated using `h=(n-1)*p`, linear interpolation, and
`p=.25/.75`. Bounds are `Q1 - 1.5*IQR` and `Q3 + 1.5*IQR`, inclusive. Values
outside the bounds are outliers; the clean benchmark is the arithmetic mean of
clean values. Fewer than two clean observations publish no benchmark. If
`IQR=0`, only values equal to the bound are clean.

Classification is Green/Positive when `Percent <= CleanIndustryAverage` and
Red/Negative when greater. Equality is Green; there is no Neutral metric state.
Without a benchmark the metric is `Unclassifiable`.

Rank order over the complete eligible industry is: `PositiveMetricCount`
descending; P/E, P/S, and equilibrium percent ascending with nulls last;
`ValidMetricCount` descending; immutable `CompanyId` ascending. Any member
with at least one classifiable metric is eligible, including `0/3`, `1/3`, and
`1/2`. A `0/0` member is unranked and consumes no Top-N slot. `GlobalRank` is
persisted before limiting or pagination.

## Watch Rules

Durable states are `NotWatching`, `EntryPending`, `Watching`, and
`ExitPending`; `Inconclusive` is an evaluation outcome. A valid watch day is a
complete Published calculation whose three clean averages each have at least
two clean observations. Entry requires all three averages `<100`; exit requires
all three `>100`; exactly `100` satisfies neither.

The configured `EntryConsecutiveSnapshots` and
`ExitConsecutiveSnapshots` control thresholds, defaulting to 3. Inconclusive
days pause without incrementing or resetting. A valid day satisfying neither
predicate clears pending counters and returns to the applicable stable state.

## Semantic, Read, and Clarification Rules

The four capabilities are v1, use their approved required/optional slots, and
route to the persisted `IndustryRelativeValuationRead` contract. Precedence is
explicit pair comparison, explicit industry ranking, symbol-vs-industry
language, then industry summary. The executor accepts canonical IDs, the
selected published calculation, and a bounded limit only; it rejects LLM-
provided formulas, SQL, ranks, averages, or colors.

Feature 119 outcomes are `Resolved`, `Ambiguous`, `NotFound`, and `Missing`,
with `InvalidIndustryMembership` and `DifferentIndustries` for this feature.
Feature 120 stores the pending required slot, candidate canonical IDs, and
optimistic version; follow-up replay is idempotent and resumes the original
intent. A task switch does not reuse stale pending state.

## Acceptance Criteria

### Resolution and semantic contracts

#### AC-01 Canonical industry identity

Given an explicit industry name, approved alias, or NADPCO external ID, when it
is resolved, then the result uses the NADPCO provider scope and stable
`ExternalId`; display title alone is never the key, and same-title candidates
across provider scopes are `Ambiguous`.

#### AC-02 Symbol-derived industry

Given a resolved canonical symbol without an explicit industry, when a supported
request is made, then the industry is derived from its canonical catalog row;
missing classification is `Unclassified`, not a new industry.

#### AC-03 Pair resolution

Given two symbols, when a pair request is classified, then both are resolved
before comparison. Equal canonical `IndustryId` returns persisted results;
different industries return `DifferentIndustries` and no comparison result.

#### AC-04 Membership mismatch

Given a symbol and an explicit industry that do not match, when resolved, then
the result is `InvalidIndustryMembership`, reports the symbol’s actual
canonical industry, and enters clarification rather than generic no-data.

#### AC-05 Capability registration

Given the Feature 118 registry, then all four named capabilities exist at v1
with the approved required/optional slots and route to the persisted
`IndustryRelativeValuationRead` use case. Precedence is pair, explicit
ranking, symbol-vs-industry, summary.

#### AC-06 Read boundary and ownership

Given a normal AI read, when the executor runs, then it accepts only canonical
IDs, selected published calculation identity, and bounded limit; it makes no
provider call or formula/statistical calculation and rejects LLM formula, SQL,
rank, average, or color inputs. Plain P/S and explicit P/S gauge requests keep
their existing ownership.

#### AC-07 Clarification lifecycle

Given `Resolved`, `Ambiguous`, `NotFound`, `Missing`, `InvalidIndustryMembership`,
or `DifferentIndustries`, then the response uses the corresponding Feature 119
outcome. Feature 120 persists the pending required slot and candidate canonical
IDs with optimistic versioning; a one-turn follow-up resumes the original
intent idempotently, while a task switch discards stale pending state.

### Provider contracts and provenance

#### AC-08 P/S projection reuse

Given Feature 114’s accepted P/S circle payload, then Feature 125 reuses the
existing acquisition/authentication/resilience path and publishes one
provider-fact projection: circle `close` is `CurrentPS` and circle `avg` is
`HistoricalAveragePS`. `BoundaryAverage` is never used as the relative P/S
baseline, and the projection inherits accepted payload timestamps, hash,
provider identity, and source observation ID. No duplicate P/S worker exists.

#### AC-09 P/E contract

Given a request to `/api/pe/circle-chart-data/{isin}`, then the accepted object
contains `a,b,c,d,e,f,close,start,end,min,max,avg`; `close` maps to
`CurrentPE`, `avg` to `HistoricalAveragePE`, and unknown additive fields are
ignored. The bounded validated payload is retained under the approved audit
convention.

#### AC-10 Equilibrium contract

Given a request to `/api/equilibrium/gauge/{isin}`, then the accepted business
fields are `enticker,ticker,per,lastcaldate,close,balance,maxbalance,minbalance,volume,date,growth,a,b,c,d,e,f`;
`close` maps to `CurrentMarketPrice` and `balance` to `EquilibriumPrice`.
Other fields do not change calculation, and provider ticker/ISIN identity is
checked against the canonical link.

#### AC-11 Provider validation and failures

Given either new provider contract, then valid fixtures are accepted with
decimal values, while bounded-body overflow, malformed JSON, non-finite or
unusable numeric values, and identity mismatch produce deterministic distinct
quality/readiness outcomes and unusable facts do not calculate. 404 and 204
produce `NotFoundOrNoData`; auth failure after existing retry, 429, timeout,
network failure, and 5xx each retain their distinct approved failure outcome.

#### AC-12 Provider policies and logging

Given P/S, P/E, or equilibrium acquisition, then the existing CyclicalWaves
authentication, retry, timeout, rate-limit, and telemetry policies are reused.
Raw payloads are bounded and retained only under the audit convention, never
written to ordinary logs, and raw symbols/ISINs are bounded or hashed in
telemetry labels.

#### AC-13 Source fact contract

Given an accepted fact, then it persists equivalent fields for `CompanyId`,
`ProviderName`, `SourceKind`, `SourceObservationId`, `CurrentValue`,
`ReferenceValue`, `FetchedAtUtc`, `PersistedAtUtc`, `SourceWatermark`,
`PayloadHash`, `Readiness`, `QualityCode`, and `IdentityEvidence`.

#### AC-14 Fact immutability

Given an accepted source observation, then its identity/hash and values are
immutable. Changed provider values create a new observation/version and never
destructively rewrite evidence previously used by a calculation.

#### AC-15 Calculation provenance

Given a calculation input, then the source barrier and calculation rows record
the exact selected source observation ID, payload hash, and deterministic
watermark for every canonical member and source kind.

### Normalization, benchmark, and ranking

#### AC-16 P/E normalization

Given finite positive P/E gauge `close` and `avg`, then decimal normalization is
`close / avg * 100`; `PE_TTM` is not used.

#### AC-17 P/S normalization

Given finite positive P/S gauge `close` and `avg`, then decimal normalization is
`close / avg * 100`; circle `avg`, never `BoundaryAverage`, is used.

#### AC-18 Equilibrium normalization

Given finite positive equilibrium `close` and `balance`, then decimal
normalization is `close / balance * 100`, with no alternate quote source.

#### AC-19 Quality classification

Given missing, zero/negative, non-finite, overflowed, malformed, stale,
unavailable, or identity-mismatched operands, then the metric receives its
deterministic missing/quality reason, cannot enter a benchmark, and is not
silently converted to a valid value. Missing is neither Green nor Red; zero or
negative is Red with `InvalidNonPositiveInput`.

#### AC-20 R7 benchmark

Given valid normalized values, then R7 interpolation, multiplier 1.5, inclusive
bounds, decimal arithmetic, and metric-specific outlier exclusion produce the
persisted `IQR-R7-1.5-v1` result. For 2, 3, and 4 values, results match the
approved R7 samples; for `IQR=0`, only values equal to the bound are clean.

#### AC-21 Benchmark minimum

Given fewer than two clean values, including one clean, all missing, or all
non-positive inputs, then no benchmark is published and no fallback mean or
classification is invented. The metric is `Unclassifiable`.

#### AC-22 Classification and outlier result

Given a publishable benchmark, then `Percent <= average` is Green/Positive and
greater is Red/Negative, including equality as Green. An outlier remains in the
member result with normalized value, `IsOutlier=true`,
`ExcludedFromIndustryBenchmark`, clean benchmark, classification when
classifiable, and source/calculation evidence.

#### AC-23 Complete ranking

Given a complete industry snapshot, then all eligible members are ranked by the
approved lexicographic order, including global null ordering and CompanyId
final tie-breaker, and `GlobalRank` is persisted before Top-N or pagination.

#### AC-24 Partial metrics and 0/0

Given `0/3`, `1/3`, or `1/2` classifiable coverage, then the member remains
eligible with its positive/valid counts. Given `0/0`, then it remains in full
membership views as `InsufficientData/Unranked`, has null rank, and consumes no
Top-N slot. Missing is never treated as Red.

#### AC-25 Result limits and stable pagination

Given no limit, then `DefaultResultLimit=3` is used. `DefaultResultLimit` is
valid only in `1..100`; `MaximumResultLimit` is valid only in `1..1000` and
defaults to 100. A request above maximum is rejected or clarified, never
silently truncated. Full ranking precedes limiting, and repeated reads of the
same published calculation return stable page membership and ranks.

### Calculation date, readiness, publication, and correction

#### AC-26 Calculation date and source barrier

Given a calculation boundary, then `CalculationDate` uses the repository’s
Tehran business-date convention. One captured barrier selects a source version
and watermark for every canonical member/source kind; configured freshness is
checked against `PersistedAtUtc`, with the approved default of 26 hours where
applicable.

#### AC-27 Status lifecycle

Given calculation processing, then `Pending` means inputs/barrier assembly and
is not AI-visible; `Ready` means required rows/barriers exist but is not
AI-visible; `Published` means a complete validated selected version and is
normal-AI-visible; `Inconclusive` is retained for history/diagnostics, is not a
normal financial result or valid watch day; and `Failed` means no consistent
version was produced and is not AI-visible.

#### AC-28 Freshness and partial generations

Given stale, unavailable, or partially updated sources, then mixed generations
cannot publish a misleading complete snapshot. A company metric may remain
missing in an otherwise valid Published snapshot, but an industry without one
of the three required benchmarks is Inconclusive and cannot be a valid watch
day.

#### AC-29 Daily historical snapshot

Given a successful configured business date, including unchanged provider
values, then a new daily historical snapshot with membership, barrier, source,
algorithm, and calculation evidence is persisted. Rerunning the date is a
recalculation, not a second calendar/watch day.

#### AC-30 Atomic publication

Given a candidate calculation, then selected publication atomically includes
the industry calculation, metric rows, company member/rank rows, and required
watch evaluation/outbox/transition evidence. A failure before commit leaves the
version non-current and safe to retry; no partial version is selected.

#### AC-31 Version identity and correction

Given a calculation, then `(CalculationDate, IndustryId, CalculationVersion)`
and `(CalculationId, CompanyId)` are unique. Same barrier/hash retry is a no-op.
A changed provider observation creates a new version; the prior Published
version remains auditable, and the new version becomes current only after
complete atomic publication. A lower-readiness attempt cannot replace current
Published data.

#### AC-32 Current selection and watch reference

Given multiple versions for one date, then the highest valid version under the
explicit selected-current marker is used. Watch transitions reference the
selected calculation ID; increasing `CalculationVersion` on the same date
does not count as another valid watch day.

### Watch state machine

#### AC-33 Configured entry threshold

Given `NotWatching` and a valid Published day with all three averages `<100`,
then entry counter increments, exit counter does not, and state becomes/stays
`EntryPending` until `EntryConsecutiveSnapshots` is reached. At the exact
configured threshold, including values 1, 2, 3, and greater than 3, state
transitions to `Watching` with transition evidence.

#### AC-34 Configured exit threshold

Given `Watching` and a valid Published day with all three averages `>100`, then
exit counter increments, entry counter does not, and state becomes/stays
`ExitPending` until `ExitConsecutiveSnapshots` is reached. At the exact
configured threshold, including values 1, 2, 3, and greater than 3, state
transitions to `NotWatching` with transition evidence.

#### AC-35 Neutral valid day

Given a valid day satisfying neither entry nor exit, including any required
average exactly 100, then the relevant pending counter is cleared, both
pending counters are cleared, both predicates are false, and the state returns to its applicable stable state
(`NotWatching` or `Watching`). Entry and exit counters never advance together.

#### AC-36 Inconclusive pause

Given an Inconclusive evaluation, then an Inconclusive outcome is persisted,
durable state and counters remain unchanged, and the pending streak is paused,
not incremented or reset. A later valid qualifying day continues it.

#### AC-37 Watch idempotency

Given watch evaluation identity `(IndustryId, CalculationId, EvaluationKind)`,
repeated or concurrent processing cannot increment a streak twice or create a
duplicate transition. A corrected same-date version references the selected
calculation and cannot become a second valid day solely because its version
number increased.

### Read, audit, operations, and configuration

#### AC-38 Published read contract

Given a successful Published read, then the response includes IndustryId and
display name, CalculationDate, selected CalculationVersion, freshness/status,
CompanyId/symbol, PEPercent, PSPercent, EquilibriumPercent, each clean
benchmark, each classification, every applicable missing/invalid/outlier
reason, PositiveMetricCount, ValidMetricCount, GlobalRank,
TotalRankedMembers, AlgorithmVersion, and RankVersion.

#### AC-39 Unavailable and diagnostic read contract

Given an unavailable benchmark, then classification is `Unclassifiable` with a
reason. Given `0/0`, rank is null with `InsufficientData`. Given a diagnostic or
history read, publication/status, source provenance, barriers, hashes,
membership, algorithm/rank versions, boundaries, quality reasons, and watch
references remain available without making an unready result normal-AI-visible.

#### AC-40 Historical auditability

Given a published calculation, correction, source fact, or watch transition,
then prior evidence is not deleted and the history identifies date/version,
selected publication, source observations/hashes/watermarks, membership hash,
algorithm/rank versions, benchmark boundaries, quality/outlier reasons, and
referenced watch calculation.

#### AC-41 Leases and worker behavior

Given scheduled processing, then ingestion and calculation use separate leases;
the calculation lease key is `industry-relative-valuation:{CalculationDate}`;
only one worker can publish a date; the worker is bounded and respects
cancellation/deadline; and a provider failure for one company does not abort
unrelated company processing.

#### AC-42 Operations evidence

Given ingestion or calculation activity, then existing retry, timeout,
rate-limit, correlation, and data-sync activity conventions are reused. Counts,
source barriers, statuses, and failure codes are observable through persisted
activity/status evidence, and telemetry labels do not expose raw high-cardinality
symbol, ISIN, or industry names.

#### AC-43 Configuration validation

Given startup configuration, then `Enabled` is supported and validation accepts
only: `DailyCadenceMinutes` 1440..10080; `SourceFreshnessHours` 1..168 with
default 26; `IqrMultiplier` 1.5..5 with default 1.5;
`DefaultResultLimit` 1..100 with default 3;
`MaximumResultLimit` 1..1000 with default 100; and entry/exit streak values
1..30 with defaults 3. Startup rejects out-of-range values and a default limit
above maximum. Algorithm and rank versions are persisted so configuration
changes do not silently rewrite history.

#### AC-44 No recommendation

Given any ranking, classification, watch, or relative-valuation result, then
the feature may explain persisted valuation context and evidence but must not
recommend buying or selling solely from this feature.

## Required Deterministic Fixture Coverage

The implementation test suite must include fixtures for valid P/E, P/S
`avg` versus `BoundaryAverage`, valid equilibrium, every provider failure/no-
data outcome, malformed/oversized/identity-mismatched payloads, R7 samples of
2/3/4, zero IQR, inclusive bounds, one clean, all missing, all non-positive,
metric-specific outlier, `0/0`, `0/3`, nullable and total ties, stable Top-N
pagination, stale/partial generations, correction, concurrent retry,
inactive/moved/unclassified membership, ambiguous and wrong industries,
different-industry pairs, watch thresholds of 1 and greater than 3,
Inconclusive pause, exact 100, and same-date duplicate prevention.

## Traceability

| Acceptance criteria | Approved design sections / existing authority |
|---|---|
| AC-01–07 | §2, §3, §10; Features 118, 119, 120 |
| AC-08–15 | §4; Feature 114 provider contract |
| AC-16–25 | §1, §6, §7 |
| AC-26–32 | §5, §8 |
| AC-33–37 | §9 |
| AC-38–40 | §5, §8, §10 |
| AC-41–43 | §11; §12 |
| AC-44 | §13 |
| Fixture coverage | §4, §6, §7, §9, §10, §11, §12 |

## Definition of Ready for Task Breakdown

`tasks.md` may be created only after this document passes the independent
Stage 3.1 review. Each implementation task must map to one or more acceptance
criteria and an approved design section. No locked formula, canonical
membership rule, benchmark algorithm, ranking order, publication rule, or watch
threshold may change without a new approved design decision.
