# Bug: Provider Sales Data Semantics and Unit Normalization Are Mixed Between Noavaran Amin and CyclicalWaves

## Summary

The implementation incorrectly treats Noavaran Amin monthly sales data and CyclicalWaves sales metrics as if they have the same semantic shape and unit.

They are fundamentally different:

1. **Noavaran Amin monthly activity data is raw line-item data**

   * Sales are reported per product/service line item.
   * Company-level sales must be calculated by summing `productSaleValue` / service sales values.
   * Values are reported in **million Rials**.
   * Aggregation must happen during ingestion/recalculation, not at AI query time.

2. **CyclicalWaves data is already precomputed**

   * Fields such as `last_month_sale`, `last_year_same_month_sale`, `average_12_month_sale`, `last_quarter_sale`, `pe`, and `ps` are already calculated by the provider.
   * These values must be persisted as-is, without recalculating them from line items.
   * Values are reported in **Rials**.

The current specs do not clearly define this distinction, which caused incorrect implementation decisions.

---

## Root Cause

The related specs describe monthly sales lookup and composite sales answers, but they do not explicitly define:

* provider-level metric semantics;
* whether a provider field is raw or precomputed;
* monetary unit normalization rules;
* which provider requires aggregation;
* which provider must be treated as a passthrough source;
* where unit conversion must happen.

Because of this, the implementation conflated:

* Noavaran Amin raw monthly activity line items;
* CyclicalWaves already-computed metric fields.

---

## Required Provider Semantics

### Noavaran Amin

Noavaran Amin monthly activity responses contain detailed product/service rows.

Example source shape:

```json
{
  "companyTSESymbol": "کچاد",
  "outputTypeId": 0,
  "productSales": [
    {
      "productTitle": "فولاد",
      "productSaleValue": 49185110,
      "outputTypeTitle": "دوره یک ماهه"
    },
    {
      "productTitle": "کنسانتره آهن",
      "productSaleValue": 27990014,
      "outputTypeTitle": "دوره یک ماهه"
    }
  ]
}
```

Rules:

* `productSaleValue` is a raw line-item value.
* Company-level sales = sum of all relevant `productSaleValue` values for the company/month/output type.
* Source unit is **million Rials**.
* Persisted canonical monetary values must be normalized according to the platform canonical unit policy.
* The aggregation must happen during ingestion/recalculation.
* The AI query path must never sum `MonthlyReportLineItems`.

Required aggregation keys:

* `ExternalCompanyId`
* reporting month / period
* `OutputType`
* provider/source metadata

Required output types:

* `OutputType = 0`: latest single-month sales
* `OutputType = 1`: fiscal-year-to-date sales
* `OutputType = 4`: fiscal-year-to-previous-month sales

Same-month prior fiscal-year comparison must be resolved from persisted `OutputType = 0` monthly aggregates for the same company and same fiscal/Shamsi month one year earlier.

---

### CyclicalWaves

CyclicalWaves responses contain already-computed company-level metrics.

Example source shape:

```json
{
  "ticker": "کچاد",
  "last_month_sale": 90879722000000,
  "penultimate_month_sale": 52144839000000,
  "last_year_same_month_sale": 69220219000000,
  "average_12_month_sale": 57549286500000,
  "last_quarter_sale": 249211279000000,
  "last_year_same_quarter_sale": 206545150000000,
  "pe": 9.73,
  "ps": 2.14
}
```

Rules:

* These fields are provider-precomputed facts.
* They must not be recalculated from Noavaran monthly line items.
* They must be persisted as source-marked/pass-through `DerivedMetrics` or equivalent lookup-ready facts.
* Source unit for monetary sales fields is **Rials**.
* No multiplication by 1,000,000 must be applied to CyclicalWaves monetary values.
* Ratios such as `pe` and `ps` are unitless and must be stored as-is.

Required CyclicalWaves persisted metrics include, at minimum:

* `CYCLICALWAVES_LAST_MONTH_SALE`
* `CYCLICALWAVES_PENULTIMATE_MONTH_SALE`
* `CYCLICALWAVES_LAST_YEAR_SAME_MONTH_SALE`
* `CYCLICALWAVES_AVERAGE_12_MONTH_SALE`
* `CYCLICALWAVES_LAST_QUARTER_SALE`
* `CYCLICALWAVES_LAST_YEAR_SAME_QUARTER_SALE`
* `PE_TTM` or mapped `pe`
* `PS_TTM` or mapped `ps`

Metric naming may differ if the governed semantic catalog already defines equivalent canonical codes, but the implementation must keep provider provenance and calculation policy clear.

---

## Required Fix

Update the implementation and all related specs so they clearly separate:

| Provider      | Data Shape                        | Calculation Requirement                       | Source Unit   |
| ------------- | --------------------------------- | --------------------------------------------- | ------------- |
| Noavaran Amin | Raw product/service line items    | Must aggregate during ingestion/recalculation | Million Rials |
| CyclicalWaves | Precomputed company-level metrics | Must persist as-is                            | Rials         |

---

## Specs That Must Be Reviewed and Updated

Update these specs if they exist in the repository:

* `057-nadpco-monthly-activity-freshness-and-sales-lookup`
* `059-monthly-activity-output-type-segmentation`
* `067-cyclicalwaves-company-mapping`
* `068-companies-first-refactor`
* `069-noavaran-monthly-sales-composite-lookup`
* any CyclicalWaves metric sync spec that defines quarterly/monthly sales, PE, PS, or margin ingestion
* semantic catalog / derived metrics specs if they define metric units or calculation policies

The specs must explicitly document:

1. Raw vs precomputed provider semantics.
2. Monetary unit conversion rules.
3. Provider-specific calculation policy.
4. Storage target and metric-code mapping.
5. Query-time restriction: no live aggregation.
6. Regression tests for unit normalization and provider passthrough.

---

## Acceptance Criteria

* Noavaran Amin `productSaleValue` is summed per company/month/output type during ingestion/recalculation.
* Noavaran Amin monetary values are normalized from million Rials according to the platform canonical monetary unit.
* CyclicalWaves sales fields are persisted as provider-precomputed facts and are not recalculated.
* CyclicalWaves monetary values are treated as Rials.
* CyclicalWaves `pe` and `ps` are stored as-is as unitless ratios.
* Query-time Symbol Lookup only reads persisted facts.
* AI query path does not aggregate `MonthlyReportLineItems`.
* Monthly production/sales Symbol Lookup responses omit market quote context: no `LATEST_PRICE`
  and no `DAILY_CHANGE_PCT`.
* Response evidence identifies provider, source unit, canonical unit, and calculation policy.
* Regression tests prove:

  * Noavaran aggregation sums all line-item `productSaleValue` values.
  * Noavaran million-Rial values are normalized correctly.
  * CyclicalWaves Rial values are not multiplied again.
  * CyclicalWaves precomputed fields are persisted as passthrough metrics.
  * Composite latest-sales lookup returns correct values with correct provider provenance.
  * Monthly production/sales lookup responses do not include latest price or daily price change.
