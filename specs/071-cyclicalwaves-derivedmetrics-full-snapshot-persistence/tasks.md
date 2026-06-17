# Tasks - CyclicalWaves DerivedMetrics Full Snapshot Persistence

## Task 1 - Audit Current CyclicalWaves Pipeline

Status: Completed

Requirements:

* Review `/api/custom-filtering/ticker/{ticker}` payload mapping.
* Identify which fields are normalized but not persisted to `DerivedMetrics`.
* Keep provider scope CyclicalWaves-only.

Acceptance:

* No Noavaran monthly-activity code path is modified.

## Task 2 - Add Missing Passthrough Metric Policies

Status: Completed

Requirements:

* Add self-source/passthrough policies and registered calculators for CyclicalWaves base quarterly
  line items: `REVENUE`, `NET_PROFIT`, `GROSS_PROFIT`, `OPERATING_PROFIT`.
* Keep existing passthrough policies for `AVG_4Q_REVENUE`, margin metrics, `PE_TTM`, `PS_TTM`, and
  `AVG_12M_MONTHLY_SALES`.
* Preserve existing growth policies.

Acceptance:

* Latest, penultimate, and same-quarter-prior-year CyclicalWaves quarterly line items persist to
  `DerivedMetrics`.

## Task 3 - Add Missing CyclicalWaves Monthly Average Mapping

Status: Completed

Requirements:

* Persist `last_year_average_12_month_sale` when present.
* Use CyclicalWaves Rials passthrough evidence.
* Do not change Noavaran monthly report aggregation or unit normalization.

Acceptance:

* CyclicalWaves monthly average snapshots are available in `DerivedMetrics`.

## Task 4 - Evidence and Unit Validation

Status: Completed

Requirements:

* Every CyclicalWaves persisted monetary metric carries CyclicalWaves source evidence with
  `sourceUnit=Rials`, `canonicalUnit=Rials`, and
  `unitNormalizationPolicy=cyclicalwaves-precomputed-rials-passthrough-v1`.
* Ratio metrics carry ratio passthrough evidence.
* No CyclicalWaves-derived metric cites `NoavaranCurrentApi` as source evidence.

Acceptance:

* Regression tests inspect `SourceEvidenceJson`.

## Task 5 - Regression Tests

Status: Completed

Requirements:

* Use the کچاد sample values.
* Assert `DerivedMetrics` contains:
  * `AVG_12M_MONTHLY_SALES = 57549286500000`
  * `MONTHLY_SALES = 90879722000000`
  * prior/same-month sales from `last_year_same_month_sale = 69220219000000`
  * `REVENUE = 249211279000000`
  * `AVG_4Q_REVENUE = 265915619500000`
  * `NET_PROFIT = 75257854000000`
  * `GROSS_PROFIT = 62289927000000`
  * `OPERATING_PROFIT = 54150691000000`
  * `NET_PROFIT_MARGIN = 30.2`
  * `GROSS_PROFIT_MARGIN = 24.99`
  * `OPERATING_PROFIT_MARGIN = 21.73`
  * `PE_TTM = 9.73`
  * `PS_TTM = 2.14`
* Add a repository/database-level guard that fails if CyclicalWaves-derived rows contain only the
  narrow monthly-sales metric set.

Acceptance:

* Focused unit/integration tests pass.
* Backend Release build passes.
