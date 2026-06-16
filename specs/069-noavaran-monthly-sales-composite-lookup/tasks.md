# Tasks - Noavaran Monthly Sales Composite Lookup

## Task 1 - Harden Symbol Lookup Metric Parsing

### Objective

Prevent composite LLM metric expressions from breaking alias resolution.

### Requirements

* Update LlmSymbolLookupParser instructions.
* Enforce shortest user-written metric phrase extraction.
* Add explicit examples for:

  * آخرین فروش
  * فروش ماهانه
* Add negative examples for:

  * آخرین فروش / sales / revenue

### Acceptance Criteria

* Composite metric output resolves correctly.
* Existing metric lookups remain unchanged.

---

## Task 2 - Implement Composite Alias Normalization

### Objective

Support defensive parsing of slash-separated metric expressions.

### Requirements

* Split metric expressions on `/`.
* Trim and normalize segments.
* Resolve aliases independently.
* Prefer exact user-language matches.
* Fall back to translated aliases only when required.

### Acceptance Criteria

* Composite expressions resolve deterministically.
* No ambiguity is introduced for existing aliases.

---

## Task 3 - Introduce Persisted Monthly Sales Lookup Metrics

### Objective

Expose all required Noavaran sales facts as lookup-ready metrics.

### Requirements

Add metric definitions for:

* MONTHLY_SALES_SINGLE_MONTH
* MONTHLY_SALES_YTD
* MONTHLY_SALES_YTD_PREVIOUS_MONTH

Define previous fiscal-year same-month lookup strategy:

Either:

* MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH

or

* deterministic retrieval of prior-year MONTHLY_SALES_SINGLE_MONTH

### Acceptance Criteria

* Metrics are persisted in DerivedMetrics.
* Metrics support ExternalCompanyId lookup.

---

## Task 4 - Extend Metric Recalculation Pipeline

### Objective

Precompute all required sales facts during ingestion.

### Requirements

Aggregate and persist:

* OutputType=0
* OutputType=1
* OutputType=4

Support prior fiscal-year same-month comparison.

### Acceptance Criteria

* No query-time aggregation exists.
* Recalculation generates lookup-ready metrics.

---

## Task 5 - Implement Monthly Sales Composite Response Model

### Objective

Support grouped sales responses.

### Requirements

Create response model containing:

* metric label
* value
* reporting period
* source
* output type
* freshness metadata

### Acceptance Criteria

* Multiple sales facts can be returned in a single response.

---

## Task 6 - Extend Symbol Metric Lookup Service

### Objective

Return composite monthly sales results.

### Requirements

Update lookup service to:

* read persisted monthly sales facts
* assemble grouped response
* resolve prior fiscal-year comparison

### Acceptance Criteria

* No MonthlyReportLineItems aggregation occurs.
* All required sales facts are returned.

---

## Task 7 - AI Response Rendering

### Objective

Present monthly sales snapshots consistently.

### Requirements

Render:

* Latest Monthly Sales
* Same Month Previous Fiscal Year
* Fiscal Year To Date Sales
* Fiscal Year To Previous Month Sales

Include:

* source evidence
* freshness indicators
* confidence

### Acceptance Criteria

* Users receive a complete monthly sales snapshot.

---

## Task 8 - Automated Tests

### Requirements

Add:

* parser tests
* alias resolution tests
* metric persistence tests
* lookup service tests
* composite response tests
* regression tests

### Acceptance Criteria

* Composite alias bug cannot regress.
* Monthly sales snapshot behavior is fully covered.
