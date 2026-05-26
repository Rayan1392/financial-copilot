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

### Growth

A percentage change between a current period and a comparison period.

Examples:

- latest quarter vs same quarter last year,
- latest month vs previous month,
- latest month vs same month last year,
- latest 3-month period vs previous 3-month period.

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

## Ambiguity Rules

When the user does not specify comparison type:

- Financial statement growth defaults to YoY.
- Monthly sales growth defaults to YoY.
- If the phrase includes "previous month", use MoM.
- If the phrase includes "latest quarter", use the latest 3-month statement period.
- If the phrase includes "latest financial statement", use latest available statement regardless of period length, but show period type.

When ambiguity materially affects results, the AI facade response should return `needsClarification = true` with suggested interpretations.
