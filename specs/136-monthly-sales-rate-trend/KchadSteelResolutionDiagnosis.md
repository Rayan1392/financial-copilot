# KCHAD Steel Product Resolution Diagnosis

Date: 2026-10-04

Scope: read-only investigation of `روند فروش فولاد کچاد`. No production code or data was changed. Accepted normalized PostgreSQL rows were queried read-only. Resolver outcomes below are reproduced from the actual accepted candidate set and the resolver's current matching predicate; no DataAdmin operation or provider request was run.

## 1. Company scope

Company resolution obtains KCHAD's `ExternalCompanyId=3`. Both `GetAvailablePeriodsAsync` and `GetPeriodAsync` in `EfCoreMonthlyProductComparisonRepository` filter on that exact external company id, `ReportType=ProductSales`, `OutputType=0`, and `IsAccepted=true`. Candidate enumeration uses only those period results.

**Cross-company candidates: NO.** A product from another company cannot participate through the current repository path. This is not a cross-company-scope bug.

## 2. Actual candidates for `فولاد`

Historical accepted KCHAD type-0 rows yield two distinct normalized title/unit identities. ProviderProductCode and ProviderProductId are absent for both; generated row-local `ProductCode` values are not used as stable identity by `MonthlyProductTrendCalculator.ProductKey`.

| ProductKey | Exact raw title variants retained | Normalized title | Unit | Provider code | Provider ID | First period | Last period | Observation periods / rows | In 1405/06? | Match to `فولاد` |
|---|---|---|---|---|---|---|---|---:|---|---|
| `NoavaranCurrentApi:3:TITLE:فولاد|UNIT:تن` | `فولاد`; `فولاد ` | `فولاد` | `تن` | absent | absent | 1404/01 (2025-03-21) | 1405/06 (2026-08-23) | 18 / 18 | Yes, 1 row | EXACT |
| `NoavaranCurrentApi:3:TITLE:فولاد فروش صادراتی|UNIT:تن` | `فولاد فروش صادراتی`; `فولاد  فروش صادراتی` | `فولاد فروش صادراتی` | `تن` | absent | absent | 1404/01 (2025-03-21) | 1405/06 (2026-08-23) | 18 / 18 | Yes, 1 row | CONTAINS (partial title match) |

“Observation periods / rows” counts the accepted normalized rows under each normalized title/unit identity, not just periods with non-null sales quantities. The repository's product key fallback is based on provider name, company id, normalized title, and normalized unit. Raw whitespace differences therefore do not create separate keys here.

## 3. Exact versus partial matching and the export-sales product

Yes, `فولاد فروش صادراتی` is a candidate for the direct query `فولاد`. It enters through `title.Contains(requested, OrdinalIgnoreCase)` in `MonthlyProductTrendQueryUseCase.Matches`.

The implementation computes one match set by applying the predicate `exact normalized title OR title contains request OR exact provider code`. It checks whether this combined set has more than one candidate before selecting anything. It has no exact-title-first phase. Since `فولاد` exactly matches the first candidate while also being contained in `فولاد فروش صادراتی`, the result is `Ambiguous`.

For query `فولاد فروش صادراتی`, only that normalized title matches, so it resolves uniquely. A broader request such as `گندله` currently also includes all title identities containing that token, not only the exact title.

## 4. Historical ProductKey analysis

For the exact normalized title `فولاد`, the local accepted type-0 history contains one identity: provider code and provider id are absent, unit is `تن`, and normalized title is consistently `فولاد`. Although the generated `ProductCode` fingerprint changes across reports, the Feature 136 key generator deliberately ignores that generated field for stable identity. The exact-title observations form one company-scoped ProductKey spanning the available history; no overlapping second exact-title ProductKey is evidenced.

For `فولاد فروش صادراتی`, history likewise forms one separate title/unit ProductKey. It is a genuinely distinct product title in the source rows; it overlaps the `فولاد` key temporally and is not a provider-ID/code continuity claim. Both products coexist as separate rows in the latest accepted revision. The evidence supports **B) genuinely distinct products**, not historical identity churn or duplicate normalized identities.

Each period's accepted source row references the accepted revision for that period. In `1405/06`, both rows come from the same accepted report `ProductSales:3:1405-06:output-0`; its revision status is `Accepted`, `IsAccepted=true`, and the report source checksum is the same for the two rows. This confirms that the two titles coexist in one source revision, rather than being artifacts of two competing revisions.

## 5. Matching precedence

Current order in code is effectively:

1. compute exact-title, contains-title, and exact-code matches together;
2. return ambiguous if more than one match;
3. otherwise select the sole match.

This differs from the approved safe precedence in the request. One exact normalized-title match must take precedence over partial/prefix/contains matches. Stable identifier matching is also not exposed as an explicit identifier query in this user phrase; neither candidate has a provider code/id to prefer.

## 6. Accepted `1405/06` product-mix comparison

The actual accepted KCHAD `ProductSales`, `OutputType=0` report for Shamsi `1405/06` (`2026-08-23` start) has two rows whose title contains `فولاد`:

| Raw title | Normalized title | ProductKey | Accepted rows | Report / revision |
|---|---|---|---:|---|
| `فولاد` | `فولاد` | `NoavaranCurrentApi:3:TITLE:فولاد|UNIT:تن` | 1 | `ProductSales:3:1405-06:output-0`, Accepted |
| `فولاد فروش صادراتی` | `فولاد فروش صادراتی` | `NoavaranCurrentApi:3:TITLE:فولاد فروش صادراتی|UNIT:تن` | 1 | `ProductSales:3:1405-06:output-0`, Accepted |

Exact normalized title count: **1 row**. Title-containing count: **2 rows**. The broader product-mix entries the user listed are consistent with reading the same accepted type-0 source; the two فولاد rows share the exact report identifier and source checksum, so this is not a discrepancy between separate accepted source revisions. The resolver's candidate universe includes accepted type-0 history across periods, while the revenue mix is a single-period view; for these two identities the same-period source revision agrees.

## 7. Direct resolver outcomes on accepted candidates

The following outcomes are reproduced by applying the actual current `Matches` predicate to all distinct company-scoped candidates in the accepted normalized type-0 data:

| Product text + company | Outcome under current resolver | Candidate count | Candidates |
|---|---|---:|---|
| `فولاد` + KCHAD | Ambiguous | 2 | `فولاد`; `فولاد فروش صادراتی` |
| `فولاد فروش صادراتی` + KCHAD | Resolved | 1 | `فولاد فروش صادراتی` |
| `گندله` + KCHAD | Ambiguous | 5 | `ارسالی گندله به احیاء`; `کنسانتره مصرف شده در تولید گندله`; `گندله`; `گندله فروش صادراتی`; `نرمه گندله (pellet fine)` |

These are resolver-predicate outcomes for the current local accepted rows, rather than a newly executed V2 conversational request. The direct predicate is the exact implementation used to form matches in the real V2 use case.

## 8. Root-cause classification

**MATCH_PRECEDENCE_BUG**

The ambiguity for `فولاد` is caused by evaluating partial `Contains` matches alongside exact matches. The exact title is uniquely represented, but a distinct longer product title is allowed to override that exactness and make the query ambiguous. The `1405/06` data confirms both products are legitimate separate identities, so they should not be merged.

## 9. Smallest safe fix

Adjust only product candidate matching to use precedence tiers: first return exact normalized-title candidates (and compatible-unit disambiguation); only if that tier is empty should the resolver consider approved aliases and then partial/prefix/contains matches. Do not merge ProductKeys or change identity generation. This makes `فولاد` resolve to its sole exact-title product while leaving `فولاد فروش صادراتی` independently resolvable. If there are multiple exact candidates after stable-key grouping, preserve the existing ambiguity response.

Likely files for a separately authorized fix:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyProductTrendQueryUseCase.cs` — tiered match selection.
- `tests/FinancialCopilot.UnitTests/MonthlyProductTrendQueryUseCaseTests.cs` (or the existing Feature 136 query-use-case test file) — add exact-vs-partial, long-title direct lookup, and historical duplicate-key cases.

No code or test files were modified during this diagnosis.

FEATURE_136_KCHAD_STEEL_RESOLUTION_DIAGNOSED
