# Monthly Sales Direct Lookup Path

## Purpose

This document explains where the response for a question like `آخرین فروش ماهانه خفنر؟` comes from, which code path serves it, and which other question types use the same path.

## Short Answer

The response is **not fetched live from an external API at query time**.

At answer time, the system reads **persisted derived monthly-sales metrics** from the backend database through the symbol-lookup path:

1. `POST /api/ai/v1/query`
2. AI orchestration detects a **direct metric lookup**
3. `lookup_symbol_metrics` path parses the symbol and metric
4. `EfCoreSymbolMetricLookupService` resolves the company
5. The service reads persisted rows from `DerivedMetrics`
6. The response prose and table are built from those persisted values

The monthly-sales values in `DerivedMetrics` are populated earlier by the ingestion/normalization pipeline from **Noavaran Amin monthly report data**, primarily under these persisted source names:

- `NoavaranCurrentApi`
- `NoavaranArchiveSql`

Both source names belong to the same logical vendor: **NoavaranAmin**.

## API Entry Point

The public request enters here:

- `src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs`

Relevant route:

- `POST /api/ai/v1/query`

The controller forwards the user message to:

- `IAiQueryOrchestrationService.ExecuteAsync(...)`

## Active Runtime Path for `آخرین فروش ماهانه خفنر؟`

The repository currently supports two orchestration implementations, but the direct monthly-sales lookup path is explicitly implemented in the Microsoft Agent Framework V2 workflow as well.

Primary runtime file:

- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`

For a query like `آخرین فروش ماهانه خفنر؟`, the flow is:

1. `AiFacadeController.Query(...)` receives the message.
2. The orchestration workflow checks whether the message is a **monthly activity trend/chart request** first.
3. If it is **not** a trend/chart request, the workflow checks whether it is a **direct metric lookup**.
4. Monthly-sales phrases such as `آخرین فروش`, `فروش ماهانه`, `فروش YTD`, and `متوسط فروش 12 ماهه` qualify as direct metric lookup terms.
5. The workflow calls `lookupAdapter.LookupAsync(...)`.
6. The lookup adapter calls the symbol lookup parser.
7. The parser resolves the question to one or more metric codes such as `MONTHLY_SALES`.
8. `EfCoreSymbolMetricLookupService.LookupAsync(...)` reads the persisted data.
9. The answer consistency/prose builder turns the result into the final sentence and table.

## Intent and Routing Rules

### 1. Direct metric routing

Monthly-sales direct lookup terms are recognized in these files:

- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
- `src/backend/FinancialCopilot.Application/Scanner/DirectMetricRoutingRegistry.cs`
- `src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs`
- `src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs`

Important behavior:

- Explicit monthly-sales phrasing routes to `MONTHLY_SALES`, not generic quarterly `REVENUE`.
- The workflow intentionally passes the **original user message** into the lookup path so phrases like `آخرین فروش` are not rewritten into another metric family.

### 2. Parser behavior

`LlmSymbolLookupParser` contains explicit direct monthly-sales terms including:

- `آخرین فروش ماهانه`
- `فروش ماهانه`
- `فروش ماهیانه`
- `فروش آخرین ماه`
- `فروش این ماه`
- `مبلغ فروش`
- `آخرین فروش`
- `فروش YTD`
- `فروش YTD تا ماه قبل`
- `فروش YTD تا ماه گذشته`
- `متوسط فروش 12 ماهه`
- `متوسط فروش ۱۲ ماهه`
- `میانگین فروش 12 ماهه`
- `میانگین فروش ۱۲ ماهه`
- `فروش ماه`
- `فروش`

Important nuance:

- In the parser and routing layers, explicit monthly-sales phrase families are forced toward the **monthly snapshot metric family**.
- Bare ambiguous sales language is handled carefully so the monthly-sales path is preserved for the supported monthly phrases.

## Data Retrieval Path

The query-time data read happens here:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreSymbolMetricLookupService.cs`

This service does the following:

1. Resolves the symbol/company to an `ExternalCompanyId`
2. Reads metric rows from `DerivedMetrics`
3. Reads company metadata from `Companies`
4. Expands `MONTHLY_SALES` into the companion monthly-sales columns when appropriate
5. Formats the returned monetary values in **million Rials** for the direct-lookup table/prose

For monthly-sales questions, the lookup service may return these columns:

- `MONTHLY_SALES`
- `AVG_12M_MONTHLY_SALES`
- `MONTHLY_SALES_YTD`
- `MONTHLY_SALES_YTD_PREVIOUS_MONTH`
- `MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH`

The same-month-last-year column is included when the user wording implies comparison phrases such as:

- `مشابه`
- `دوره قبل`
- `سال قبل`
- `مدت مشابه`

## Where `MONTHLY_SALES` Comes From Before Query Time

The persisted monthly-sales values are produced by the ingestion/normalization pipeline, not by the AI query endpoint itself.

Key file:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NormalizedMetricInputSources.cs`

Relevant implementation:

- `MonthlySalesMetricInputSource`

What it does:

1. Reads normalized monthly reports and line items
2. Aggregates sales line items into the metric `MONTHLY_SALES`
3. Emits normalized metric observations with source provenance
4. Those observations are later persisted into `DerivedMetrics`

The unit evidence in the same file shows that for Noavaran Amin monthly monetary metrics:

- source unit: `MillionRials`
- canonical unit: `Rials`

That is why the direct monthly-sales response sentence is rendered like:

- `آخرین فروش ماهانه خفنر برابر با 1,124,787 میلیون ریال است.`

## Provider / Vendor Source

Provider identity is defined here:

- `src/backend/FinancialCopilot.Application/FinancialData/Providers/ProviderSourceModel.cs`

Important mapping:

- `NoavaranCurrentApi` -> logical vendor `NoavaranAmin`
- `NoavaranArchiveSql` -> logical vendor `NoavaranAmin`

So if the monthly-sales data came from either of those persisted source names, the business source family is the same:

- **Noavaran Amin**

## Final User-Facing Sentence Builder

The deterministic sentence for a single-symbol monthly-sales lookup is built here:

- `src/backend/FinancialCopilot.Application/Scanner/AnswerConsistencyServices.cs`

That file contains the monthly-sales sentence template used for symbol lookup answers:

- Persian single-result monthly sales sentence
- fallback sentence for monthly-sales companion columns such as YTD or 12-month average

## Question Types That Use This Same Path

The following query families go through the **direct symbol metric lookup path** and read persisted monthly-sales metrics via `EfCoreSymbolMetricLookupService`.

### A. Latest monthly sales

Examples:

- `آخرین فروش ماهانه خفنر؟`
- `فروش ماهانه خفنر`
- `فروش ماهیانه خفنر`
- `آخرین فروش خفنر`
- `فروش آخرین ماه خفنر`
- `فروش این ماه خفنر`
- `مبلغ فروش خفنر`
- `فروش ماه خفنر`

Primary metric:

- `MONTHLY_SALES`

### B. Year-to-date monthly sales

Examples:

- `فروش YTD خفنر`
- `فروش از ابتدای سال مالی خفنر`
- `جمع فروش از ابتدای دوره خفنر`
- `فروش از ابتدای دوره خفنر`

Primary metric:

- `MONTHLY_SALES_YTD`

### C. Year-to-date sales up to previous month

Examples:

- `فروش YTD تا ماه قبل خفنر`
- `فروش YTD تا ماه گذشته خفنر`
- `فروش از ابتدای سال مالی تا ماه گذشته خفنر`
- `جمع فروش از ابتدای دوره تا ماه قبل خفنر`

Primary metric:

- `MONTHLY_SALES_YTD_PREVIOUS_MONTH`

### D. 12-month average monthly sales

Examples:

- `متوسط فروش 12 ماهه خفنر`
- `متوسط فروش ۱۲ ماهه خفنر`
- `میانگین فروش 12 ماهه خفنر`
- `میانگین فروش ۱۲ ماهه خفنر`

Primary metric:

- `AVG_12M_MONTHLY_SALES`

### E. Same month last year / prior comparable month

Examples:

- `فروش ماه مشابه سال قبل خفنر`
- `فروش مشابه دوره قبل خفنر`
- `فروش ماه مشابه دوره قبل خفنر`
- `فروش مدت مشابه خفنر`

Primary metric behavior:

- lookup is still rooted in the monthly-sales family
- the service may add `MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH`

### F. Follow-up replies inside the same conversation

Examples:

- User turn 1: `آخرین فروش ماهانه`
- User turn 2: `خفنر`

or:

- User turn 1: `متوسط فروش 12 ماهه`
- User turn 2: `خفنر`

These can still stay on the direct metric lookup path because the workflow and lookup adapter support **direct metric follow-up resolution** from recent context.

## Question Types That Do Not Use This Path

### 1. Monthly trend / chart requests

These go to the monthly activity trend use case, not the direct symbol metric lookup table path.

Examples:

- `روند فروش ماهانه خفنر`
- `نمودار فروش ماهانه خفنر`
- `مقایسه فروش سال جاری و سال گذشته خفنر`
- `فروش امسال خفنر نسبت به پارسال`
- `فروش خفنر نسبت به میانگین ۱۲ ماهه`
- `گزارش فروش با نمودار خفنر`

Relevant file:

- `src/backend/FinancialCopilot.Application/AI/Orchestration/MonthlyActivityTrendIntentRules.cs`

That path returns chart-ready trend data, currently backed by persisted monthly-activity trend snapshots, not the direct lookup table path above.

### 2. Stock screening questions

Examples:

- `سهام با رشد فروش بالا`
- `نمادهایی که فروش ماهانه خوبی دارند`

These go through the scanner path, not symbol metric lookup.

### 3. Analysis/report questions

Examples:

- `تحلیل خفنر`
- `آخرین گزارش ماهانه خفنر`

These go through the analysis/comprehensive-analysis path, not the direct monthly-sales metric lookup path.

### 4. Quarterly revenue / income-statement sales questions

Examples:

- `درآمد فصلی خفنر`
- `فروش فصلی خفنر`
- `درآمد خالص عملیاتی خفنر`

These belong to the income-statement `REVENUE` family rather than the monthly-sales snapshot family.

## Practical Conclusion

For the specific question:

- `آخرین فروش ماهانه خفنر؟`

the runtime path is:

1. `POST /api/ai/v1/query`
2. `AiFacadeController`
3. AI orchestration direct metric lookup branch
4. `SymbolLookupToolAdapter` / `LlmSymbolLookupParser`
5. `EfCoreSymbolMetricLookupService`
6. database read from `DerivedMetrics` and `Companies`
7. deterministic response prose from `AnswerConsistencyServices`

The underlying business data source family is:

- **Noavaran Amin**

The persisted source names that feed this monthly-sales metric family are:

- `NoavaranCurrentApi`
- `NoavaranArchiveSql`
