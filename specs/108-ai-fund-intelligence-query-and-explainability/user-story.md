# User Story — AI Fund Intelligence Query and Explainability

## Status
`[ ]` Proposed

## Feature
Enable Persian natural-language questions about fund portfolios, institutional activity, income attribution, risk exposures, cross-fund consensus, and historical conviction quality through the existing AI facade.

## Story

As a FinancialCopilot user,

I want to ask questions about investment funds and their disclosed holdings in Persian,

so that I can research fund behavior, stock-level institutional activity, and supporting evidence without manually reading complex monthly Excel workbooks.

## Business Context

Features 100–107 create canonical, normalized, and derived fund-intelligence data. This feature adds governed intent detection, retrieval tools, structured responses, Persian rendering, citations, billing, telemetry, and missing-data feedback through the existing Microsoft Agent Framework workflow and `POST /api/ai/v1/query`.

The AI must read persisted data. It must not reopen Excel files or recalculate historical analytics during a user query.

## Dependencies

- Features `100`–`107`.
- Features `009`, `010`, `017`, `018`, and `028`.
- Features `047` and `056` for AI orchestration.
- Features `072` and `074` for governed intent/alias registries.

## In Scope

### Fund-level questions

- portfolio overview and asset allocation;
- top holdings;
- top purchases and sales;
- new positions and full exits;
- position increases/decreases;
- sector allocation and rotation;
- cash/deposit, commodity, derivative, concentration, and liquidity exposure;
- income composition and contributors/detractors;
- valuation-adjustment and portfolio-valuation quality;
- historical conviction-quality evidence.

### Security-level questions

- funds holding a symbol;
- funds buying/selling/increasing/reducing a symbol;
- new fund entries and full exits;
- cross-fund accumulation/distribution score;
- historical early-entry funds for a symbol or industry.

### Market-level questions

- most accumulated/distributed shares;
- sectors with the strongest fund rotation;
- funds becoming more risk-on or defensive;
- high-confidence consensus with minimum coverage filters.

## Persian Query Examples

- پرتفوی آخر صندوق مشترک آگاه را خلاصه کن
- پنج سهم بزرگ صندوق آگاه چیست؟
- صندوق آگاه این ماه چه سهم‌هایی خریده؟
- صندوق آگاه از چه سهم‌هایی کامل خارج شده؟
- وزن کدام صنایع در صندوق آگاه بیشتر شده؟
- سود صندوق بیشتر از سود نقدی بوده یا رشد قیمت دارایی‌ها؟
- کدام دارایی‌های صندوق تعدیل قیمت شده‌اند؟
- کدام صندوق‌ها این ماه فملی را خریده‌اند؟
- کدام سهم بیشترین خرید صندوقی را داشته؟
- آیا خرید صندوق‌ها در کگل سه ماه متوالی افزایش یافته؟
- کدام صندوق‌ها سابقه بهتری در ورود زودهنگام به سهم‌های فلزی داشته‌اند؟

## Out of Scope

- Personal investment advice.
- User-owned portfolio accounting.
- Real-time fund order flow.
- Answers from unresolved or superseded reports without clear warning.
- LLM-created calculations, SQL, fund rankings, or source values.

## Acceptance Criteria

1. All questions route through the existing AI facade and billing lifecycle.
2. Fund and company resolution use canonical identities with clarification on ambiguity.
3. The configured/latest accepted source revision and report period are explicit.
4. Results are generated from persisted read models and snapshots.
5. Every financial fact includes source report, period, freshness/import time, and evidence/citation.
6. Delayed monthly disclosure is stated in stock-level and consensus answers.
7. Base consensus and quality-weighted consensus are clearly distinguished.
8. Historical conviction results show methodology, sample size, horizon, and limitations.
9. Missing/low-coverage data produces a transparent partial/unavailable response and Feature 028 feedback.
10. The AI cannot change deterministic numbers or convert descriptive analytics into a recommendation.

## Intent Proposal

```text
FUND_PORTFOLIO_OVERVIEW
FUND_ASSET_ALLOCATION
FUND_TOP_HOLDINGS
FUND_TOP_BUYS
FUND_TOP_SELLS
FUND_NEW_POSITIONS
FUND_FULL_EXITS
FUND_POSITION_CHANGES
FUND_SECTOR_ROTATION
FUND_RISK_EXPOSURE
FUND_INCOME_ATTRIBUTION
FUND_VALUATION_QUALITY
SYMBOL_FUND_HOLDERS
SYMBOL_FUND_ACTIVITY
CROSS_FUND_ACCUMULATION
CROSS_FUND_DISTRIBUTION
FUND_CONVICTION_QUALITY
EARLY_CONVICTION_FUNDS
```

## Response Contract Proposal

```csharp
public sealed record FundIntelligenceAiResponse(
    string Intent,
    FundReference? Fund,
    CompanyReference? Company,
    FundReportPeriodReference Period,
    IReadOnlyList<FundIntelligenceResultItem> Items,
    FundIntelligenceSummary Summary,
    decimal? ConfidenceScore,
    IReadOnlyList<DataCitation> Citations,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SuggestedAction> SuggestedActions);
```
