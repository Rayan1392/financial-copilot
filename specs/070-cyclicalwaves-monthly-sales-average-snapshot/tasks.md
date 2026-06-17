# Tasks - CyclicalWaves Monthly Sales Average Snapshot

## Task 1 - Document Provider Boundary

Requirements:

* Mark this feature as CyclicalWaves-only.
* State that spec 069 remains authoritative for Noavaran monthly activity data.
* Do not change `MonthlyReports`, `MonthlyReportLineItems`, `OutputType` 0/1/4 logic, or the
  Noavaran composite snapshot.

Acceptance:

* Specs clearly separate CyclicalWaves behavior from Noavaran behavior.

## Task 2 - Select CyclicalWaves Display Layout

Requirements:

* Detect CyclicalWaves monthly sales rows from persisted `DerivedMetrics` evidence.
* For default latest/monthly sales questions, display:
  `فروش ماهانه`, `متوسط فروش ۱۲ ماهه`, `فروش YTD`, `فروش YTD تا ماه قبل`.
* For explicit same-month previous-period/year questions, display:
  `فروش ماهانه`, `فروش ماه مشابه دوره قبل`, `فروش YTD`, `فروش YTD تا ماه قبل`.
* Keep Noavaran default behavior unchanged.

Acceptance:

* CyclicalWaves defaults to `AVG_12M_MONTHLY_SALES`.
* Noavaran defaults to prior fiscal-year same-month sales.
* Explicit same-period requests do not use the average metric.

## Task 3 - Display Values and Labels

Requirements:

* Read `AVG_12M_MONTHLY_SALES` from `DerivedMetrics`; do not aggregate line items.
* Divide `AVG_12M_MONTHLY_SALES` by 1,000,000 for display in the monthly-sales table.
* Use the mandatory Persian display title `متوسط فروش ۱۲ ماهه`.
* Never expose `AVG_12M_MONTHLY_SALES`, `Average 12 Month Sales`, or
  `Average 12-Month Monthly Sales` as a user-facing label/header.

Acceptance:

* Table headers use only the Persian business label.
* Display formatting follows the shared financial number policy with no redundant `.00`.

## Task 4 - Preserve Table-Only Rendering

Requirements:

* Keep backend compatibility unit text as `Unit: million Rials` where needed.
* Keep frontend localization to `واحد: میلیون ریال` as small muted table metadata.
* Suppress LLM-generated prose, clarification suggestions, fallback text, and false missing-data
  narratives when monthly-sales table values exist.
* Omit `LATEST_PRICE` and `DAILY_CHANGE_PCT`.

Acceptance:

* The table remains the main answer.
* Persian users do not see raw `Unit: million Rials`.

## Task 5 - Automated Tests

Add focused regression tests:

* CyclicalWaves default query `آخرین فروش کچاد چقدر بوده؟` contains
  `متوسط فروش ۱۲ ماهه` and does not contain `فروش ماه مشابه دوره قبل`.
* CyclicalWaves explicit query `فروش ماه مشابه دوره قبل کچاد چقدر بوده؟` contains
  `فروش ماه مشابه دوره قبل` and does not contain `متوسط فروش ۱۲ ماهه`.
* Unit conversion divides `AVG_12M_MONTHLY_SALES` by 1,000,000.
* Missing prior-year same-month rows remain Missing/null and do not fall back to
  `AVG_12M_MONTHLY_SALES`.
* Header regression proves internal/English average labels are absent.
* Noavaran monthly-sales regression still proves the spec 069 default prior-period layout.

Acceptance:

* Focused backend integration tests pass.
* Existing frontend unit-label localization test remains green.
