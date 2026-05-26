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
- Versioned financial metric definitions, bilingual aliases, calculation policies, and metric dependency metadata required for Phase 1 calculations/explanations.
- Data provider sync metadata.
- Conversations and Messages for AI chat history.
- AI query executions and routed tool activity.
- Scanner query plans/executions as internal evidence when the Scanner Tool is selected.
- Scanner result snapshots where needed for usage analytics and explainability.
- Customer accounts, subscription/entitlement state, approved organization credit lines, and invoice-account profiles required for authorization and charging.
- Usage reservations and immutable usage ledger entries associated with AI query execution.
- Wallet balance projections derived from ledger entries for performant balance reads.
- Correlated AI/provider/tool workflow trace evidence needed under approved telemetry retention policy.

### Persist Later

- Full textual analysis reports.
- Embeddings.
- News/events.
- Codal disclosure summaries.
- Watchlist events.
- Portfolio snapshots.
- Historical derived-feature snapshots and feature computation jobs once feature-based intelligence is promoted into delivery scope.
- AI evaluation datasets, run results, and regression baselines.
- Consent-managed long-term, portfolio-aware, research, preference, or watchlist memory.

For future large-scale textual/research retrieval, evolve toward:

```text
PostgreSQL
+ Elasticsearch/OpenSearch
+ Vector Storage
```

Do not introduce Elasticsearch or vector storage for the Phase 1 scanner unless real query/search requirements justify it. PostgreSQL with appropriate indexes remains the initial structured scanner and billing store.

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
FinancialMetricDefinitions
MetricAliases
MetricCalculationPolicies
MetricDependencies
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
CustomerAccounts
InvoiceAccounts
CreditLines
SubscriptionPlans
Subscriptions
WalletBalanceProjections
UsageReservations
UsageLedgerEntries
FinancialTransactions
ApiClients
AiExecutionTraces
ToolExecutionTraces
```

`ScannerQueries`, `ScannerQueryPlans`, `ScannerExecutions`, and `ScannerResultItems` are internal execution/audit data. User-facing chat history is retrieved from `Conversations` and `Messages`.

`UsageLedgerEntries` and `FinancialTransactions` are append-only accounting truth for the `FinancialCopilot.Billing` bounded context. `WalletBalanceProjections` are rebuildable read models and must not be treated as authoritative ledger state.

`FinancialMetricDefinitions`, aliases, policies, and dependencies form the versioned semantic catalog for calculation and explanation. `DerivedMetrics` reference the metric and policy versions used so historical observations remain auditable.

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
  -> Resolve/store semantic metric and policy versions
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
- `financial-features.recalculate.requested`, future feature-foundation event
- `scanner.cache.refresh.requested`
- `textual-report.embedding.requested`
- `billing.usage-reservation.expiry.requested`
- `billing.invoice.generate.requested`
- `billing.payment.reconcile.requested`
- `billing.wallet-projection.rebuild.requested`

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

Metric examples are not an exhaustive or hardcoded catalog. EPS, P/E, margins, growth measures, and cash-flow measures represent only an initial subset. All supported metrics should resolve through a versioned `FinancialMetricDefinition`, canonical `MetricCode`, registered calculation strategy, and versioned policy. Use extensible contracts such as `IFinancialMetricCalculator`, `IFinancialMetricRegistry`, `IMetricAliasResolver`, and `IMetricCalculationPolicyProvider`; avoid formula routing through large application-service branching blocks.

Scanner query plans and Explainable Answers retain semantic metric identifiers and calculation-policy versions. Persian and English aliases map into the same definition only when they have the same governed financial meaning.

## Future Derived Feature Storage

The platform may later persist deterministic, historical `FeatureSnapshot` values for feature definitions such as momentum, liquidity, volatility, relative strength, growth consistency, and earnings quality. Feature computations use versioned metric inputs and may be recalculated by worker/RabbitMQ workflows.

This is a future-compatible computation boundary, not a Phase 1 commitment to deploy a full feature store, training pipeline, or online ML serving platform.

## Provider Abstraction

Create provider interfaces:

```csharp
public interface IMarketDataProvider
public interface IFinancialStatementProvider
public interface IMonthlyProductionSalesProvider
public interface ITextualAnalysisProvider
```

Infrastructure implements these interfaces for each third-party provider.

## AI Model Provider Boundary

Financial-data providers are separate from LLM/model providers. AI model adapters may target hosted execution such as OpenAI or Anthropic/Claude, a future Abravran integration once its contract is available, or local execution such as Ollama.

Define provider-neutral model contracts for:

```csharp
public interface IAiModelClient
public interface IAiModelProviderResolver
public interface IAiProviderCapabilityRegistry
public interface IAiExecutionTelemetrySink
```

Persist normalized internal AI execution evidence when required for audit and Billing, including provider/model alias, requested capabilities, status, timing, fallback outcome, and provider-supplied usage metrics where available. Do not store provider secrets in operational data or make scanner correctness depend on a provider-specific response format.

## AI Evaluation And Observability Data

Internal evaluation may later persist versioned `GoldenQuestion`, `GoldenAnswer`, `EvaluationDataset`, `PromptVersion`, `EvaluationRun`, `EvaluationScore`, and `RegressionResult` records. These records test prompt/orchestration/scanner changes against approved outcomes and are not on the production query critical path.

Operational telemetry should support OpenTelemetry-compatible traces and metrics for AI workflows, tool execution, provider attempts, latency, normalized token/usage measures, retries, fallbacks, and errors. Prompt/answer trace persistence requires tenant-aware redaction, privacy, and retention policy. Telemetry assists investigation and provider comparison; it is not the accounting source of truth.

## Conversation Memory Data Boundary

Phase 1 persists Conversations and Messages only. Future memory such as user preferences, portfolio-aware context, research memory, or watchlist memory requires consent, tenant/subject scoping, sensitive-data protection, auditability, and deletion/revocation rules before persistence or use in orchestration.

## Billing Data Boundary

Use the dedicated `FinancialCopilot.Billing` bounded context for both organization partners and direct consumers:

- Organization partners may use prepaid, postpaid, or hybrid balance plus approved credit-line policies.
- Direct consumers use subscription allowances and top-ups, with no overdraft by default.
- Store partner-provided `externalUserId` values only as tenant-scoped usage attribution identifiers.
- Keep internal operation/provider/compute cost data separate from displayed credits and currency transactions.
- Apply idempotency to reservations, ledger commits, releases, refunds, payment callbacks, and invoice settlements.

## API Licensing Note

Before persisting third-party data, confirm provider contract terms. Some providers allow caching but not long-term redistribution. If persistence is restricted, store only derived metrics and source metadata, or use a provider-approved cache duration.
