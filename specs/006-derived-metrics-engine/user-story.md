# User Story — Derived Metrics Engine

## Story

As a scanner engine,  
I need deterministic calculated metrics,  
so that filters like net profit growth, sales growth, P/E, and P/S are accurate and testable.

## Acceptance Criteria

- Net profit growth YoY can be calculated.
- Monthly sales growth YoY can be calculated.
- Monthly sales growth MoM can be calculated.
- TTM sales can be calculated.
- TTM earnings/EPS can be calculated when data exists.
- P/E can be calculated using documented policy.
- P/S can be calculated using documented policy.
- Missing or invalid denominator cases are handled safely.
- Calculation policy is stored with derived metric result.
- Metric calculations consume concepts and period policies from `003-financial-domain-model` and normalized source data produced by `005-data-ingestion-and-normalization`.
- Valuation metric observations retain price source/as-of metadata; `008-scanner-execution-engine` separately resolves the displayed latest/live-or-fallback table price.
- Unit tests cover normal and edge cases.

## Technical Notes

- AI must never calculate financial metrics.
- All formulas must be deterministic backend code.
