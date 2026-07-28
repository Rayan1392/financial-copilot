# Tasks - Noavaran Monthly Sales Composite Lookup

## Task 1 - Harden Symbol Lookup Metric Parsing

Prevent composite LLM metric expressions from breaking alias resolution.

Requirements:

* Update `LlmSymbolLookupParser` instructions.
* Enforce shortest user-written metric phrase extraction.
* Add examples for `آخرین فروش`, `فروش ماهانه`, and slash-separated phrases such as
  `آخرین فروش / sales / revenue`.

Acceptance:

* Composite metric output resolves correctly.
* Existing metric lookups remain unchanged.

## Task 2 - Implement Composite Alias Normalization

Support defensive parsing of slash-separated metric expressions.

Requirements:

* Split metric expressions on `/`.
* Trim and normalize segments.
* Resolve aliases independently.
* Prefer exact user-language matches.
* Fall back to translated aliases only when required.

Acceptance:

* Composite expressions resolve deterministically.
* No ambiguity is introduced for existing aliases.

## Task 3 - Introduce Persisted Noavaran Monthly Sales Lookup Metrics

Expose the required Noavaran sales facts as lookup-ready persisted metrics.

Required facts:

* `MONTHLY_SALES` from `OutputType=0`.
* prior fiscal-year same-month sales by deterministic retrieval of prior-year `MONTHLY_SALES`.
* `MONTHLY_SALES_YTD` from `OutputType=1`.
* `MONTHLY_SALES_YTD_PREVIOUS_MONTH` from `OutputType=4`.

Acceptance:

* Metrics are persisted in `DerivedMetrics`.
* Metrics support `ExternalCompanyId` lookup.
* No CyclicalWaves metric-selection rule is introduced in this Noavaran spec.

## Task 4 - Extend Metric Recalculation Pipeline

Precompute required sales facts during ingestion.

Requirements:

* Aggregate and persist `OutputType=0`, `OutputType=1`, and `OutputType=4`.
* Support prior fiscal-year same-month comparison from persisted single-month rows.

Acceptance:

* No query-time aggregation exists.
* Recalculation generates lookup-ready metrics.

## Task 5 - Implement Monthly Sales Composite Response Model

Support grouped Noavaran sales responses.

Response model fields:

* metric label;
* value;
* reporting period;
* source;
* output type;
* freshness metadata.

Acceptance:

* Multiple sales facts can be returned in a single response.

## Task 6 - Extend Symbol Metric Lookup Service

Return Noavaran composite monthly sales results without changing Noavaran provider semantics.

Requirements:

* Read persisted monthly sales facts.
* Assemble grouped response with latest month, same-month previous fiscal year, YTD, and YTD to
  previous month.
* Resolve prior fiscal-year comparison from persisted `MONTHLY_SALES`.
* Never aggregate `MonthlyReportLineItems` at query time.

Acceptance:

* No `MonthlyReportLineItems` aggregation occurs.
* Noavaran default monthly/latest sales lookup columns are exactly:
  `فروش ماهانه`, `فروش ماه مشابه دوره قبل`, `فروش YTD`, `فروش YTD تا ماه قبل`.
* Missing prior fiscal-year same-month rows render Missing/null.

## Task 7 - AI Response Rendering

Present Noavaran monthly sales snapshots consistently.

Requirements:

* Display sales monetary values in million Rials.
* Include backend compatibility unit note `Unit: million Rials`.
* Omit market quote columns from monthly production/sales responses: `آخرین قیمت`,
  `درصد تغییر آخرین قیمت`, `LATEST_PRICE`, and `DAILY_CHANGE_PCT`.
* Suppress all LLM-generated prose when the monthly-sales table has any non-missing value.
* Persisted conversation message content and structured `AssistantContent` payload obey the same
  no-extra-prose rule when the conversation is reloaded.
* Frontend maps the technical backend unit note to localized Persian table metadata:
  `واحد: میلیون ریال`.

Acceptance:

* Users receive the complete Noavaran monthly sales snapshot.
* Persisted canonical values remain in Rials, but displayed monthly-sales monetary cells are
  divided by 1,000,000.
* Unit conversion applies only to monthly-sales monetary snapshot columns.
* Persian users never see the raw English `Unit: million Rials` string in the chat UI.

## Task 8 - Automated Tests

Add or maintain:

* parser tests;
* alias resolution tests;
* metric persistence tests;
* lookup service tests;
* composite response tests;
* display-unit tests for million-Rial sales rendering and unit-note output;
* API-boundary tests proving monthly production/sales lookup responses do not include latest price
  or daily price-change columns (`آخرین قیمت`, `درصد تغییر آخرین قیمت`, `LATEST_PRICE`,
  `DAILY_CHANGE_PCT`);
* API-boundary tests proving monthly-sales snapshot query response and reloaded chat DTO narrative
  fields contain only the unit note, or are empty/null, when data exists;
* regression test for `آخرین فروش کچاد؟` proving the rendered table exists, sales values are in
  million Rials, and no missing-data or report-type prose appears;
* Noavaran regression proving the default table contains `فروش ماه مشابه دوره قبل`;
* frontend component/regression test for `آخرین فروش کچاد چقدر بوده؟` proving
  `واحد: میلیون ریال` appears in the table metadata area, `Unit: million Rials` is absent, and no
  standalone assistant paragraph renders the unit note.

Acceptance:

* Composite alias behavior cannot regress.
* Noavaran monthly sales snapshot behavior is covered.
* Monthly sales display-unit behavior cannot regress.
* Monthly production/sales market-context suppression cannot regress.
* Monthly sales no-extra-prose behavior cannot regress.
* Monthly sales frontend unit-label localization cannot regress.
