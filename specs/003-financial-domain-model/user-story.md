# User Story — Financial Domain Model

## Story

As a scanner engine,  
I need normalized financial market entities and metric definitions,  
so that financial queries can be executed consistently and explainably.

## Acceptance Criteria

- Domain includes Symbol, Company, Industry, FinancialStatement, FinancialStatementLineItem, MonthlyReport, MonthlyReportLineItem, MarketSnapshot, and DerivedMetric concepts.
- Period types support monthly, 3-month, 6-month, 9-month, 12-month, latest quarter, latest month, and TTM.
- Metrics support Phase 1 scanner requirements.
- Growth comparison supports YoY and MoM.
- Domain exposes financial metric and period semantics consumed by the Derived Metrics Engine, without owning ingestion or persisted calculation execution.
- Domain model can represent missing data and stale data warnings.
- Unit tests cover period comparison rules.

## Technical Notes

- Keep provider-specific ids as external references, not primary domain identity.
- Use value objects for SymbolCode, FiscalPeriod, MetricCode, and Percentage where useful.
- `006-derived-metrics-engine` owns deterministic calculation implementation and persistence; this story owns reusable domain vocabulary, invariants, and policy inputs.
