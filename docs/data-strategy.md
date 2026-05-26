# Data Strategy

## Problem

The product depends on financial data that is not currently stored in the application's database. Data will be obtained from third-party APIs. However, scanner queries, historical comparisons, textual/fundamental analysis, and explainable results require persisted, normalized, and reproducible datasets.

## Recommendation

Use a hybrid data strategy:

1. **Persist normalized financial data required for scanner and analytics.**
2. **Use third-party APIs on demand only for lightweight, highly fresh, or rarely used data.**
3. **Store raw provider payloads for auditability and reprocessing.**
4. **Calculate derived metrics internally to keep output reproducible.**

## Data Categories

### Must Persist for Phase 1

- Symbols.
- Companies.
- Industries.
- Market snapshots needed for valuation ratios.
- Financial statements.
- Statement line items.
- Monthly production/sales reports.
- Monthly production/sales line items.
- Derived financial metrics.
- Data provider sync metadata.
- Conversations and Messages for AI chat history.
- AI query executions and routed tool activity.
- Scanner query plans/executions as internal evidence when the Scanner Tool is selected.
- Scanner result snapshots where needed for usage analytics and explainability.
- Usage ledger entries associated with AI query execution.

### Persist Later

- Full textual analysis reports.
- Embeddings.
- News/events.
- Codal disclosure summaries.
- Watchlist events.
- Portfolio snapshots.

### On-Demand Candidate Data

- Latest price tick if provider supports low latency.
- Very fresh market board data.
- Data not used for repeatable screening.
- Data with strict provider licensing constraints that prevents persistence.

## Raw + Normalized Storage

For each provider response:

- Store raw JSON payload in `ProviderRawPayload`.
- Transform into normalized domain tables.
- Keep provider name, endpoint, external id, received timestamp, and checksum.
- Use idempotent upserts.

## Suggested Tables

```text
Symbols
Companies
Industries
MarketSnapshots
FinancialStatements
FinancialStatementLineItems
MonthlyReports
MonthlyReportLineItems
DerivedMetrics
ProviderRawPayloads
ProviderSyncRuns
ProviderSyncErrors
Conversations
Messages
AiQueryExecutions
AiToolExecutions
ScannerQueries
ScannerQueryPlans
ScannerExecutions
ScannerResultItems
UsageLedgerEntries
ApiClients
Subscriptions
UserCreditAccounts
```

`ScannerQueries`, `ScannerQueryPlans`, `ScannerExecutions`, and `ScannerResultItems` are internal execution/audit data. User-facing chat history is retrieved from `Conversations` and `Messages`.

## Data Freshness

Each API response should include:

- `asOfDate`
- `dataFreshness`
- `sourceProvider`
- `lastSyncAt`
- `warnings`

Answers returned from `POST /api/ai/v1/query` should additionally include applicable Data Citations, Confidence Score, and Usage Accounting output.

## Ingestion Pipeline

```text
Scheduled job
  -> Provider API call
  -> Raw payload saved
  -> Normalize
  -> Validate
  -> Calculate derived metrics
  -> Publish metrics-ready event
  -> Refresh cache/search index
```

## RabbitMQ Events

Recommended event names:

- `provider.symbols.sync.requested`
- `provider.market-snapshot.sync.requested`
- `provider.financial-statements.sync.requested`
- `provider.monthly-reports.sync.requested`
- `financial-metrics.recalculate.requested`
- `scanner.cache.refresh.requested`
- `textual-report.embedding.requested`

## Derived Metrics Policy

Derived metrics should be calculated by backend services, not by the LLM.

Examples:

- Net profit growth YoY.
- Monthly sales growth YoY.
- Monthly sales growth MoM.
- TTM sales.
- TTM EPS.
- P/E.
- P/S.
- Margins.

## Provider Abstraction

Create provider interfaces:

```csharp
public interface IMarketDataProvider
public interface IFinancialStatementProvider
public interface IMonthlyProductionSalesProvider
public interface ITextualAnalysisProvider
```

Infrastructure implements these interfaces for each third-party provider.

## API Licensing Note

Before persisting third-party data, confirm provider contract terms. Some providers allow caching but not long-term redistribution. If persistence is restricted, store only derived metrics and source metadata, or use a provider-approved cache duration.
