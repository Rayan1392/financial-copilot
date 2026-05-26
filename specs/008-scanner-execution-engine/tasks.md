# Tasks

- Implement `IScannerExecutionService`.
- Implement condition-to-query mapping.
- Define table column and row DTOs with value type, formatting hint, source timestamp, and freshness/source status.
- Implement `IScannerResultColumnPolicy` for default columns, relevant query-metric columns, explicit overrides, and the maximum of 10 displayed data columns.
- Define and implement `IMarketQuoteResolver` for live quote selection with previous-completed-trading-day fallback.
- Implement batch screener result projection for symbol, resolved price, daily change percentage, market capitalization, selected metric values, score, and matched conditions.
- Implement `IScannerResultRanker`.
- Implement missing data warnings.
- Wire the Scanner Tool into `IAiQueryOrchestrator` and the AI facade response.
- Emit operation/cache/provider execution facts needed by `IUsageChargeCalculator` without coupling scanner services to wallet or ledger persistence.
- Add integration tests through `POST /api/ai/v1/query` for a `high growth and P/E below 6` request, default table columns, live/fallback price provenance, explicit column overrides, and 10-column validation; do not add a public scanner execute endpoint.
