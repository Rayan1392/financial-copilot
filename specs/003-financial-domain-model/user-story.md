# User Story — Financial Domain Model

## Story

As a scanner engine,  
I need normalized financial market entities and metric definitions,  
so that financial queries can be executed consistently and explainably.

## Acceptance Criteria

- Domain includes Symbol, Company, Industry, FinancialStatement, FinancialStatementLineItem, MonthlyReport, MonthlyReportLineItem, MarketSnapshot, and DerivedMetric concepts.
- Period types support monthly, 3-month, 6-month, 9-month, 12-month, latest quarter, latest month, and TTM.
- Metrics support Phase 1 scanner requirements.
- Metric vocabulary is compatible with the versioned semantic-definition, alias, dependency, and calculation-policy contracts in `015-financial-semantic-layer`; Phase 1 examples do not become a closed hardcoded catalog.
- Growth comparison supports YoY and MoM.
- Domain exposes financial metric and period semantics consumed by the Derived Metrics Engine, without owning ingestion or persisted calculation execution.
- Domain model can represent missing data and stale data warnings.
- Unit tests cover period comparison rules.

## Technical Notes

- Keep provider-specific ids as external references, not primary domain identity.
- Use value objects for SymbolCode, FiscalPeriod, MetricCode, MetricVersion, and Percentage where useful.
- `006-derived-metrics-engine` owns deterministic calculation implementation and persistence; this story owns reusable domain vocabulary, invariants, and policy inputs.
- `015-financial-semantic-layer` extends this vocabulary with versioned definitions, bilingual aliases, dependency metadata, and extensible calculator registration.
