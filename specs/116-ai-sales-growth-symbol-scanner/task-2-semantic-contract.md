# Task 2 — Governed Sales-Growth Scanner Semantics

Status: Implemented

The canonical application contract is
`FinancialCopilot.Application.Scanner.SalesGrowthScannerContracts`.

It defines:

- `SalesGrowthSymbolScanner` as the `MonthlySales` / `ListMatchingSymbols`
  use-case identity;
- `PreviousMonth`, `SameMonthPreviousYear`, and
  `AveragePrevious12Months` baselines;
- `Positive`, `Percent`, and `Multiple` threshold kinds;
- the existing governed `ConditionOperator` and `FilterOrigin` types;
- versioned target-period and calculation policies;
- canonical formulas and constructor invariants.

The contract records `2.0` as a current-to-baseline multiple. That is
equivalent to `100` percentage points of growth under the canonical formulas.
Positive growth is strictly `CurrentSales > BaselineSales`.

The contract has no SQL, provider DTO, or executable user expression. Parser
mapping, target-period selection, and execution are intentionally deferred to
Tasks 3–8.
