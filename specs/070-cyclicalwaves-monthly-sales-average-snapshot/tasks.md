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

* Preserve alias routing so `آخرین فروش`, `فروش ماهانه`, and `فروش کچاد` resolve to
  `MONTHLY_SALES` / monthly-sales snapshot, not generic `REVENUE`.
* Correct LLM-emitted generic sales terms back to monthly-sales routing when the original user
  message is a latest/monthly sales question.
* Detect CyclicalWaves monthly sales rows from persisted `DerivedMetrics` evidence.
* For default latest/monthly sales questions, display:
  `نماد`, `شرکت`, `فروش ماهانه`, `متوسط فروش ۱۲ ماهه`, `فروش YTD`, `فروش YTD تا ماه قبل`.
* For explicit same-month previous-period/year questions, display:
  `نماد`, `شرکت`, `فروش ماهانه`, `فروش ماه مشابه دوره قبل`, `فروش YTD`, `فروش YTD تا ماه قبل`.
* Keep monthly production/sales answers compact and focused on operational metrics. This applies to
  latest/monthly sales, production quantity, and grouped production/sales report questions.
* Do not include market quote columns (`آخرین قیمت`, `درصد تغییر آخرین قیمت`, `LATEST_PRICE`,
  `DAILY_CHANGE_PCT`) in monthly production/sales responses.
* Keep Noavaran default behavior unchanged.

Acceptance:

* CyclicalWaves defaults to `AVG_12M_MONTHLY_SALES`.
* Noavaran defaults to prior fiscal-year same-month sales.
* Explicit same-period requests do not use the average metric.
* Default latest-sales questions do not render `REVENUE`, `فروش ماه مشابه دوره قبل`,
  `آخرین قیمت`, `درصد تغییر آخرین قیمت`, `LATEST_PRICE`, or `DAILY_CHANGE_PCT`.
* Explicit same-month previous-year questions may render `فروش ماه مشابه دوره قبل`, but still do
  not render market quote columns.
* Production/sales report questions such as `گزارش تولید و فروش کچاد` do not render market quote
  columns.

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
* Omit market quote columns from monthly production/sales responses: `آخرین قیمت`,
  `درصد تغییر آخرین قیمت`, `LATEST_PRICE`, and `DAILY_CHANGE_PCT`.

Acceptance:

* The table remains the main answer.
* Persian users do not see raw `Unit: million Rials`.
* Market quote columns remain available for valuation, screening, price, ratio, and market-statistic
  questions; they are not removed globally.

## Task 5 - Automated Tests

Add focused regression tests:

* Default sales question `آخرین فروش کچاد چقدر بوده؟` renders table columns `نماد`, `شرکت`,
  `فروش ماهانه`, `متوسط فروش ۱۲ ماهه`, `فروش YTD`, and `فروش YTD تا ماه قبل`.
* Default sales question `آخرین فروش کچاد چقدر بوده؟` does not render `آخرین قیمت`,
  `درصد تغییر آخرین قیمت`, or `فروش ماه مشابه دوره قبل`.
* Alias-routing regression proves the same query resolves to monthly-sales snapshot even when the
  parser emits generic `فروش`.
* Regression proves the default table does not contain `REVENUE`, `LATEST_PRICE`, or
  `DAILY_CHANGE_PCT`.
* CyclicalWaves explicit query `فروش ماه مشابه سال قبل کچاد چقدر بوده؟` may contain
  `فروش ماه مشابه دوره قبل`, does not contain `متوسط فروش ۱۲ ماهه`, and does not render market quote
  columns.
* Production/sales report query `گزارش تولید و فروش کچاد` renders a production/sales-focused table
  only and does not render market quote columns.
* Unit conversion divides `AVG_12M_MONTHLY_SALES` by 1,000,000.
* Missing prior-year same-month rows remain Missing/null and do not fall back to
  `AVG_12M_MONTHLY_SALES`.
* Header regression proves internal/English average labels are absent.
* Noavaran monthly-sales regression still proves the spec 069 default prior-period layout.

Acceptance:

* Focused backend integration tests pass.
* Existing frontend unit-label localization test remains green.
