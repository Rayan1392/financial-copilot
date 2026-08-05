# Task 1 — Reuse Boundaries and Gap Analysis

Status: Implemented

This review covers the current repository and the reusable foundations named by
Features 003, 006, 007, 008, 009, 015, 026, 045, 057, 069–077, and 089.

## Reuse map

| Concern | Existing boundary to reuse | Finding |
|---|---|---|
| Monthly sales values | Persisted `CompanyMonthlyActivityTrendSnapshot` contracts/repository/calculator and the normalized `MonthlyReports`/`DerivedMetrics` read models | Reuse persisted normalized data. The AI query path must not call a provider synchronously or aggregate raw provider DTOs. |
| MoM growth | `MONTHLY_SALES_GROWTH_MOM` produced by the existing derived-metric/snapshot pipeline and resolved through the financial metric registry | Reuse only when its period and rounding policy are proven equivalent to the scanner policy. |
| YoY growth | `MONTHLY_SALES_GROWTH_YOY` and the existing same-month-previous-year snapshot input | Reuse only when the scanner uses the same target period, baseline validity rules, and calculation version. |
| 12-month average | Existing monthly-sales snapshot/trend retrieval and `CompanyMonthlyActivityTrendSnapshotCalculator` | Reuse the storage/read boundary, but not the current calculation unchanged: the calculator currently averages the current month plus up to 11 prior months. Feature 116 requires a previous-12-month baseline that excludes the current observation. |
| Natural-language parsing | `LlmScannerQueryParser`, `IMetricAliasResolver`, `IScannerQueryPlanValidator`, and the governed metric registry/catalog | Extend the validated scanner boundary. Do not add a second parser or provider-specific phrase router. The current plan has metric conditions, operators, columns, and generic growth comparison, but no list objective, baseline enum, threshold kind, multiple, target-period policy, or explicit/inferred baseline origin. |
| Scanner execution | `IScannerExecutionService`, `EfCoreScannerExecutionService`, `ScannerQueryPlan`, `ScannerTableResult`, `IScannerResultColumnPolicy`, and `IScannerResultRanker` | Reuse the generic universe, AND composition, bounded execution, ranking, and pagination seams. The current execution selects the latest derived-metric row per company/metric and therefore does not enforce one common monthly evaluation period or provide Feature 116 coverage/exclusion counts. |
| Structured table | `ScannerTableColumn`, `ScannerTableCell`, `ScannerTableRow`, `ScannerExecutionFacts`, and `AnswerConsistencyValidator` | Reuse table and deterministic answer validation. Add Feature 116 metadata/dynamic comparison-column behavior through the existing contract, not a parallel table model. Current cells do not carry baseline window, policy version, or calculation evidence. |
| Web and AI facade | Existing `POST /api/ai/v1/query` orchestration and response mapping | Keep the public facade and existing single-symbol, trend, product-mix, and generic scanner routing authoritative. Feature 116 must be a scanner specialization behind that boundary. |
| Telegram | `TelegramAiAssistantAdapter`, `TelegramAssistantResponseRenderer`, and the existing pagination state conventions | Reuse rendering/pagination state and render the same validated scanner result. A Feature 116-specific compact row/footer contract and replay-safe pagination are still missing. |
| Explainability/evidence | `ExplainableAnswerContracts`, `IExplainableAnswerBuilder`, `IScannerExplanationGenerator`, scanner cells/source timestamps, and confidence services | Reuse evidence/citation and confidence plumbing. Extend facts to expose comparison, periods, freshness, coverage, policy versions, threshold origin, and exclusion reasons. |
| Billing/entitlements | `IBillingFacadeHook`, the infrastructure `AiFacadeBillingHook`, plan capability/usage reservation/finalization services, and the usage ledger | Reuse the existing AI facade billing lifecycle and tenant/actor scoping. No separate Feature 116 billing path is justified. |
| Telemetry/audit | `IAiWorkflowTelemetrySink`, AI execution telemetry sinks, correlation IDs, and billing telemetry | Reuse workflow/tool telemetry and add the Feature 116 dimensions (baseline, threshold kind/operator/value, target period, coverage, counts, stale/partial status, and parser ambiguity). |

## Confirmed non-overlap boundaries

- Single-symbol monthly-sales lookups remain on the existing symbol lookup path.
- Monthly sales trend/chart requests remain on Feature 077 and its chart response contract.
- Product revenue mix remains on Feature 075.
- Generic financial screening remains on the existing scanner plan/execution path; Feature 116 should compose with it through validated plan semantics.
- Raw provider adapters and ingestion jobs remain upstream data producers, not AI-query dependencies.

## Gaps that must be resolved before feature completion

1. Add a governed Feature 116 plan specialization or extension for `ListMatchingSymbols`, the three baselines, positive/percent/multiple thresholds, strict/inclusive operators, explicit/inferred/clarified origin, and versioned policies.
2. Define and implement a common complete monthly target period with coverage metadata; the current scanner has no market-wide common-period selection.
3. Add a provider-neutral comparison read/calculation boundary. MoM/YoY may be reused only after policy-equivalence checks; the previous-12-month average needs an input/policy that excludes the current month and requires the governed observation window.
4. Extend execution facts and evidence for missing/non-positive baselines, eligible/evaluated/matched/excluded counts, freshness, periods, and deterministic match reasons.
5. Add dynamic baseline column titles and Feature 116 pagination parity for web and Telegram without introducing a second renderer/table system.
6. Add routing precedence and regression coverage for Persian/Arabic normalization, ZWNJ, digits/decimals, percent and multiple wording, and safeguards for single-symbol/trend/product-mix requests.

## Task 1 decision

No duplicate parser, calculator, scanner engine, provider adapter, or table system is introduced by this task. The findings above establish the reuse boundaries and identify the missing semantics required by Tasks 2–16.
