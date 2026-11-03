# Tasks - Company Product Revenue Mix

## Task 1 - Database Schema

Create table:

### CompanyProductRevenueMix

Fields:

- Id
- ExternalCompanyId
- CompanySymbol
- CompanyName
- ReportYear
- ReportMonth
- FiscalEndDate
- ProductName
- ProductionQuantity
- SalesQuantity
- SalesRate
- SalesAmount
- TotalCompanySalesAmount
- RevenueSharePercentage
- ProductRank
- IsDominantProduct
- SourceProviderName
- CalculatedAtUtc

Indexes:

- ExternalCompanyId + ReportYear + ReportMonth
- CompanySymbol
- ProductRank

---

## Task 2 - Revenue Mix Calculation Service

Implement service:

CompanyProductRevenueMixCalculator

Responsibilities:

- Load latest Noavaran monthly report
- Aggregate sales amounts
- Calculate total company sales
- Calculate revenue share percentage
- Rank products
- Flag dominant products (>= 30%)

---

## Task 3 - Ingestion Integration

Integrate calculation into Noavaran monthly ingestion workflow.

After successful monthly report persistence:

- Recalculate revenue mix
- Replace previous calculation for same company/month
- Store derived rows

---

## Task 4 - Product Name Normalization

Implement normalization layer.

Goals:

- Prevent duplicate logical products
- Normalize spacing
- Normalize Persian characters
- Support future alias mapping

---

## Task 5 - Repository Layer

Create repository:

ICompanyProductRevenueMixRepository

Queries:

- Latest composition by company
- Top products by company
- Dominant products by company
- Composition by period

---

## Task 6 - AI Semantic Catalog

Add new semantic intents:

- MOST_IMPORTANT_PRODUCT
- TOP_PRODUCTS
- PRODUCT_REVENUE_COMPOSITION
- PRODUCT_CONCENTRATION

Persian examples:

- مهم‌ترین محصول کچاد چیست
- محصول اصلی کگل چیست
- ترکیب فروش محصولات فملی
- بیشترین درآمد شرکت از چه محصولی است

---

## Task 7 - AI Retrieval Provider

Create provider:

ProductRevenueMixProvider

Responsibilities:

- Resolve company
- Load latest composition
- Build structured response model

---

## Task 8 - Response Contract

Create DTOs:

ProductRevenueMixResponse

Fields:

- CompanySymbol
- CompanyName
- ReportMonth
- ReportYear
- TotalSalesAmount
- Products[]

Product:

- ProductName
- SalesAmount
- RevenueSharePercentage
- Rank
- IsDominantProduct

---

## Task 9 - AI Rendering Rules

If dominant products exist:

Display dominant products.

Otherwise:

Display top 3 products.

Example:

محصول | مبلغ فروش | سهم از کل فروش
-------|-----------|---------------
کنسانتره آهن | X | 58%
گندله | Y | 34%

---

## Task 10 - Tests

Unit Tests

- Revenue share calculation
- Ranking
- Dominant product detection
- Normalization

Integration Tests

- Noavaran ingestion integration
- Repository queries
- AI retrieval

End-to-End Tests

- مهم‌ترین محصول کچاد چیست؟
- کگل بیشتر از چه محصولی درآمد دارد؟
- ترکیب فروش محصولات فملی را نشان بده

---

## Future Enhancements (Not In Scope)

- Multi-month revenue mix trends
- Product concentration trend analysis
- Product dependency risk score
- Product diversification score
- Industry-level product comparison

---

## Implementation Extension — Deterministic Suggested Actions

### Task 11 — Typed-result selector

- Add a pure ProductRevenueMix follow-up selector that reads the successful typed result only.
- Preserve the result/display order and scan past invalid rows until two safe products are found.
- Reject duplicate normalized titles, aggregate/“other” rows, placeholders, and parser-unsafe rows.

### Task 12 — Existing capability round trips

- Validate product messages with `MonthlyProductTrendIntentRules` and the capability interpreter.
- Validate the parent-company message with `MonthlyActivityTrendIntentRules` and the interpreter.
- Require enabled target capabilities; execute no downstream use case during validation.

### Task 13 — Existing SuggestedAction and persistence path

- Build at most two `monthly_product_trend` actions, then one `monthly_activity_trend` action.
- Reuse registry-versioned SHA-256 IDs, the existing `SuggestedAction` contract, and relevance reason
  `product_revenue_mix_follow_up`.
- Set `DeterministicSuggestionsApplied` for successful typed results, including an explicit empty
  collection. Preserve generic guidance on non-success paths.

### Task 14 — Verification and report

- Add focused selector, round-trip, slot, ordering, ID, zero-action, and persistence tests.
- Run ProductRevenueMix and Features 136/137/138 regressions, routing/architecture/Telegram/frontend
  suites, and an API round-trip validation.
- Record implementation and test evidence in `ImplementationReport.md`.
