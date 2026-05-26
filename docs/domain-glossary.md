# Domain Glossary

## Core Concepts

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

### Explainability

Every scanner result should include:

- matched conditions,
- actual metric values,
- periods used,
- source report date,
- calculation policy,
- data freshness,
- confidence score.

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

When ambiguity materially affects results, API should return `needsClarification = true` with suggested interpretations.
