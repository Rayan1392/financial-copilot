# Domain Glossary

## Core Concepts

### Conversation

A user-visible AI chat session that contains Messages and may invoke different backend tools over its lifetime.

### Message

A user or assistant entry persisted within a Conversation. A user Message is submitted through the AI facade and the assistant Message stores the resulting Explainable Answer.

### AI Query Orchestrator

The Application-layer coordinator behind `POST /api/ai/v1/query`. It performs Intent Detection, Tool Routing, use-case execution, Usage Accounting, and Conversation persistence.

### Scanner Tool

The internal use case selected when a Message requests financial screening. The React UI does not call it directly.

### Symbol

A tradable stock ticker in the Iranian capital market.

### Company

The legal entity behind one or more symbols.

### Industry

The sector or industry classification of a company.

### Financial Statement

A periodic company report published for 3, 6, 9, or 12 months. Used for income statement, balance sheet, cash flow, and derived ratios.

### Monthly Production and Sales Report

A monthly report mostly relevant to production companies. Contains production volume, sales volume, sales amount, and product-level breakdowns.

### Period

A financial or reporting interval.

Supported period types:

- Monthly
- Quarterly
- Semi-annual
- Nine-month
- Annual
- Trailing twelve months
- Latest available period

The financial Domain represents latest-month and latest-quarter requests as selectors. A selector must be resolved to a closed reporting period before period comparison is calculated or a derived metric result is retained as evidence.

### Growth

A percentage change between a current period and a comparison period.

Examples:

- latest quarter vs same quarter last year,
- latest month vs previous month,
- latest month vs same month last year,
- latest 3-month period vs previous 3-month period.

The reusable Domain comparison policy supports year-over-year comparisons for closed periods and month-over-month comparisons for monthly reports. Derived metric calculation execution remains outside the Domain model.

For semantic resolution of latest-quarter net profit growth, quarter-over-quarter is represented explicitly as a candidate alongside year-over-year. If the user has not identified the comparison basis, the semantic resolver returns ambiguity rather than silently choosing either candidate.

### Net Profit Growth

Growth in net profit for a financial statement period. Default comparison for quarterly statements should be YoY unless the user asks otherwise.

### Sales Growth

Growth in sales amount. For monthly production/sales reports, default comparison should be YoY if the phrase is "last month sales growth" and MoM if the user says "compared to previous month".

### P/E

Price to Earnings ratio.

Recommended calculation policy:

- Prefer TTM earnings if available.
- Fall back to latest annual EPS only when TTM is not available.
- Persist calculation policy and show it in explanation.

### P/S

Price to Sales ratio.

Recommended calculation policy:

- Prefer TTM sales.
- Use latest market cap divided by TTM sales.
- Persist calculation policy and show it in explanation.

### Explainable Answer

Every Scanner Tool answer should include:

- matched conditions,
- actual metric values,
- periods used,
- source report date,
- calculation policy,
- data freshness,
- Confidence Score.

### Data Citation

Traceable source metadata attached to an Explainable Answer, including provider, report date, and relevant freshness timestamp.

### Confidence Score

A policy-defined confidence value reflecting interpretation and data sufficiency. It must not substitute for deterministic financial calculation.

### Usage Accounting

The record of credits/cost units and outcome associated with executing an AI facade request for a user or API client.

### Financial Metric Definition

A stable semantic identity and governed meaning for a metric, independent from source-provider fields, database columns, prompt terminology, or UI wording.

### Metric Code

A canonical executable identifier for a Financial Metric Definition, for example `NET_PROFIT_GROWTH_YOY`, `NET_PROFIT_GROWTH_QOQ`, or `PE_TTM`. Scanner plans and calculated evidence use this identifier rather than raw language or provider fields.

### Metric Version

An auditable revision of a Financial Metric Definition. Historical calculated values and explanations retain the metric version that governed them.

### Metric Calculation Policy

A deterministic, versioned policy defining how a metric is calculated, what periods/dependencies are required, and what fallback or missing-data behavior applies.

### Financial Observation Quality

Source observations and derived metric evidence can carry nullable values plus explicit missing-data or stale-data warnings, together with observation and synchronization timestamps.

### Metric Alias

A Persian, English, or alternative financial term that resolves to a governed Financial Metric Definition when its meaning is unambiguous.

### Derived Feature

A deterministic or explicitly governed computed signal based on metrics or observations, such as future momentum, liquidity, or earnings-quality scores. It is separate from a raw financial metric and can be snapshotted/versioned for future intelligence use cases.

### AI Evaluation Dataset

An internal versioned collection of Golden Questions and expected outcomes used to measure interpretation, explanation, and workflow changes over time.

### AI Execution Trace

Correlated operational telemetry across an AI query workflow, tool calls, provider attempts, timing, errors, and usage facts, subject to tenant and privacy controls.

### Conversation Memory

Optional context that may be supplied to future orchestration beyond persisted chat history. Durable or sensitive memory requires consent and does not replace authoritative user, portfolio, or billing records.

## Metric Families

### Valuation Ratios

- P/E
- Forward P/E, future phase
- P/S
- P/B, future phase
- EV/EBITDA, future phase if enterprise value data is available

### Profitability

- Net profit
- Gross profit
- Operating profit
- EPS
- Gross margin
- Operating margin
- Net margin
- ROE, future phase
- ROA, future phase

### Sales and Production

- Monthly sales amount
- Monthly sales volume
- Production volume
- Product-level sales
- Product-level production
- Average selling price

### Financial Health

- Debt ratio
- Current ratio
- Cash flow from operations
- Inventory growth
- Receivable growth

This list is illustrative only. EPS, P/E, margins, growth measures, and cash-flow measures are examples, not the full financial domain. The platform's semantic metric catalog is extensible and versioned; it must not be treated as a closed hardcoded list. Scanner parsing resolves supported Persian/English terminology through metric aliases into canonical metric codes, and derived calculations/explanations retain the selected definition and calculation-policy version.

## Ambiguity Rules

When the user does not specify comparison type:

- Financial statement growth defaults to YoY.
- Monthly sales growth defaults to YoY.
- If the phrase includes "previous month", use MoM.
- If the phrase includes "latest quarter", use the latest 3-month statement period.
- If the phrase includes "latest financial statement", use latest available statement regardless of period length, but show period type.

When ambiguity materially affects results, the AI facade response should return `needsClarification = true` with suggested interpretations.
