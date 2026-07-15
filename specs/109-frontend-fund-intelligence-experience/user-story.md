# User Story — Frontend Fund Intelligence Experience

## Status
`[ ]` Proposed

## Feature
Add fund discovery, fund portfolio intelligence, symbol-level institutional activity, and market-level fund consensus experiences to the FinancialCopilot web application.

## Story

As a FinancialCopilot user,

I want visual pages for investment funds and institutional activity,

so that I can explore normalized evidence, compare periods, and ask follow-up questions without relying only on chat responses.

## Business Context

The frontend currently includes authenticated AI, followed symbols, market insights, scanners, and data-management experiences. Fund intelligence needs dedicated product surfaces while preserving a clear distinction between:

- an investment fund's disclosed portfolio;
- the user's followed symbols;
- any future user-owned portfolio feature.

## Dependencies

- Features `105`, `106`, `107`, and `108`.
- Features `031`, `032`, `033`, `034`, `048`, and `055`.
- Feature `085` for follow-symbol actions.

## In Scope

- Fund directory/search.
- Fund detail page and reporting-period selector.
- Portfolio overview, allocation, holdings, activity, sectors, risk, income, and valuation-quality tabs.
- Source/report freshness and issue badges.
- Symbol page institutional-fund activity panel.
- Market-level fund accumulation/distribution radar.
- Historical conviction-quality methodology view.
- AI follow-up actions with fund/company/report context.
- Responsive Persian RTL UI.
- Loading, empty, partial, stale, low-confidence, and error states.

## Out of Scope

- User trade execution.
- Personal portfolio valuation.
- Public access to restricted raw workbooks.
- Charts that imply undisclosed daily fund transactions.

## Acceptance Criteria

1. Users can search/select a canonical investment fund and reporting period.
2. The fund page shows source period, disclosure/import freshness, revision, confidence, and quality warnings.
3. Holdings and activity tables distinguish beginning/ending positions and monthly disclosed purchases/sales.
4. New positions and full exits are visually distinct but not labeled recommendations.
5. Allocation/sector charts preserve unknown/unresolved coverage.
6. Symbol pages show holder/buyer/seller/new-entry/full-exit counts and contributing fund evidence.
7. Market radar shows base consensus separately from quality-weighted consensus.
8. Historical quality views include sample size, horizon, benchmark, and methodology link.
9. Follow-symbol and create-tracker actions reuse existing capabilities.
10. All screens are accessible, responsive, actor-authorized, and free of financial-advice language.

## Proposed Routes

```text
/funds
/funds/:fundId
/fund-intelligence/consensus
/symbols/:externalCompanyId (institutional activity section)
/admin/data-management/fund-reports (Feature 101 DataAdmin only)
```
