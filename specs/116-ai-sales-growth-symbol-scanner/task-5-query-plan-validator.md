# Task 5 — Scanner Query Plan and Validator

Status: Implemented

Feature 116 now extends the generic `ScannerQueryPlan` with an optional
`SalesGrowthScannerPlan` containing:

- latest eligible complete monthly-sales observation selector;
- governed baseline, threshold kind/value/operator, and origin semantics;
- market universe scope;
- optional common target period;
- deterministic growth-percent sort and direction;
- bounded page/page-size values;
- requested display columns.

`ScannerQueryPlanValidator` validates these fields without introducing SQL,
provider DTOs, or executable user expressions. Existing generic scanner plans
remain source-compatible and retain their prior validation behavior.

The `CreateInferredDefault` factory uses `SameMonthPreviousYear`, strict
`CurrentSales > BaselineSales`, no numeric threshold, and marks both baseline
and threshold origins as `InferredDefault`.

Validation is covered by `SalesGrowthScannerPlanTests`.
