# Feature 129 — Monthly Product Production and Sales Intelligence

## Objective

Enable a Financial Copilot user to compare one company’s persisted ProductSales line items for two Jalali months and understand the company sales change, product contributors, and whether a valid movement is quantity-driven, rate-driven, new/discontinued activity, or mixed.

## Persona and user story

The primary user is an Iranian market analyst or investor reviewing a company’s monthly operating report.

As a user, I want to ask in Persian which products changed a company’s monthly sales and compare production, sales quantity, and sales rate across two Jalali periods, so that I can understand reported movement using traceable, deterministic figures.

Supported examples include:

- `فروش محصولات شغدیر در ماه جاری نسبت به ماه قبل را مقایسه کن`
- `کدام محصول بیشترین افزایش فروش را داشت؟`
- `تغییر تولید و فروش شرکت را برای دو ماه مقایسه کن`
- `فروش محصولات فولاد در فروردین و اردیبهشت چگونه تغییر کرد؟`

## Fixed R1 scope

The capability resolves one company through the existing canonical resolver, selects the latest qualifying Jalali period and immediately preceding available period by default, or validates two explicit periods. It reads existing `MonthlyReports` and `MonthlyReportLineItems` through their existing relationship, filtering exactly `ReportType = ProductSales` and `OutputType = 0`.

It retains positive, zero, negative, and valid reported sales amounts. It matches products deterministically by stable valid `ProductCode` and compatible unit, otherwise normalized title plus compatible unit. Ambiguous identity or incompatible units remain separate and suppress physical decomposition while preserving valid reported sales contribution.

All totals, effects, percentages, reconciliation, driver labels, rankings, warnings, and state transitions are calculated by application code. The typed result is returned through the existing conversation flow and rendered in bounded web chat and Telegram presentations.

## Non-goals

R1 does not add tables, migrations, snapshots, revisions, accepted pointers, aliases, manifests, workers, queues, outbox/retry state, provider calls, background calculation, backfill, anomaly detection, forecasting, recommendations, direct REST endpoints, dashboards, or investment advice. It does not change existing Product Revenue Mix ingestion/calculation behavior or repair persisted data.

## Preconditions and data assumptions

The company must resolve through `ICompanyResolverService.ResolveBySymbolAsync`. Qualifying reports must exist for the selected periods. The normalized report provides `ExternalCompanyId`, Jalali period dates, report type, nullable output type, provider, and report identity. Its line items provide product code, title, unit, production quantity, sales quantity, sales amount, sales rate, and line-item identity. Values may be null or negative and must be handled explicitly.

## Functional behavior

An omitted current period selects the latest available qualifying period. An omitted comparison period selects the immediately preceding available qualifying period and never invents a missing calendar period. Explicit malformed, equal, or unavailable periods produce clarification without fallback. An unresolved company produces `CompanyNotFound` clarification. No qualifying rows produce `NoMonthlyProductData` and no invented financial result.

Company totals sum valid nullable `SalesAmount` values, including negative values. Change is current minus comparison; percentage is null when the comparison total is zero and carries `ZeroCompanyRevenueChange`.

Products match only within the resolved company. A reliable product code has priority, then normalized title plus compatible normalized unit. Fuzzy matching, aliases, and LLM matching are prohibited. Ambiguous or unit-conflicting rows remain visible as separate/unattributed items, receive stable warnings, and have null quantity/rate effects. Valid sales value remains available.

For continuing valid products:

```text
quantityEffect = (currentSalesQuantity - baseSalesQuantity) * baseSalesRate
priceEffect    = (currentSalesRate - baseSalesRate) * currentSalesQuantity
residual       = salesChange - quantityEffect - priceEffect
```

Effects reconcile within the documented decimal tolerance. Current-only products are `New`; comparison-only products are `Discontinued`. Quantity or rate effects are never invented for lifecycle rows or invalid inputs. Quantity-driven and price-driven classifications use a 60% absolute-effect share; otherwise the result is `Mixed`, or `Unclassified` when both effects are zero. Production-versus-sales difference is shown only for compatible units and is labeled inferred, never inventory.

Largest positive and negative product changes use deterministic change ordering, normalized key, source rank, and row-id tie-breakers. Negative changes and negative reported values are retained.

## Missing and ambiguous data

Blocking states are `CompanyNotFound`, `CurrentPeriodNotFound`, `ComparisonPeriodNotFound`, `EqualPeriods`, `InvalidPeriod`, and `NoMonthlyProductData`. Warnings are `ProductMatchAmbiguous`, `UnitChanged`, `MissingRate`, `InvalidQuantity`, `InvalidSalesAmount`, `PossibleDuplicateRows`, `PartialDecomposition`, and `ZeroCompanyRevenueChange`. Null remains null in the typed result and clients.

## Semantic clarification

The existing semantic/capability architecture recognizes natural Persian variation for product comparisons and keeps this capability separate from a simple monthly-sales metric lookup and published analysis. The model may extract company, periods, product, and focus, but application validation and the deterministic use case are authoritative. No rigid Persian sentence-pattern route is added.

## Web and Telegram behavior

Web chat renders bounded totals, period labels, sales change, driver, largest contributors, product rows, warning states, and evidence/source disclosure with RTL Persian labels and source units. Telegram renders a compact equivalent and uses the existing safe fallback on unexpected failure. Neither client calculates financial values or converts null to zero.

## Acceptance criteria

| ID | Acceptance criterion |
|---|---|
| AC-01 | Known company resolves through the existing resolver; unknown company returns `CompanyNotFound`. |
| AC-02 | Omitted current period selects the latest qualifying persisted period. |
| AC-03 | Omitted comparison period selects the immediately preceding qualifying available period without fabrication. |
| AC-04 | Query joins normalized reports/line items and filters `ReportType = ProductSales`, `OutputType = 0`. |
| AC-05 | Totals retain and sum positive, zero, and negative valid `SalesAmount` values. |
| AC-06 | Change is current minus comparison and zero denominator yields null percentage plus warning. |
| AC-07 | Product aggregation uses only safe deterministic identity and compatible units. |
| AC-08 | Ambiguity/unit conflict preserves value, emits warning, and suppresses decomposition. |
| AC-09 | Continuing valid products expose both-period values and changes. |
| AC-10 | Quantity effect plus price effect plus residual reconciles to product sales change. |
| AC-11 | Current-only product is `New` without invented base effects. |
| AC-12 | Comparison-only product is `Discontinued` with negative comparison sales change. |
| AC-13 | Missing/invalid inputs suppress affected effects deterministically without changing valid totals. |
| AC-14 | Production-sales difference is unit-safe and explicitly inferred, never inventory. |
| AC-15 | Largest positive/negative products use deterministic tie-breaking and retain negatives. |
| AC-16 | Driver classification follows the fixed 60% rule and zero behavior. |
| AC-17 | Typed response preserves nulls, states, warnings, periods, and evidence. |
| AC-18 | Persian product-comparison requests route correctly and simple metric lookups do not. |
| AC-19 | Web chat renders all states, bounded products, warnings, evidence, RTL values, and units. |
| AC-20 | Telegram preserves typed values/warnings in compact form and uses safe fallback. |

## Definition of Done

- All 20 acceptance criteria have automated named tests.
- The query is read-only, provider-free, company-scoped, and filtered to ProductSales/OutputType 0.
- Negative, zero, null, invalid, and incompatible-unit behavior is deterministic.
- Financial arithmetic exists only in application code.
- Typed results travel through conversation persistence/replay and both channels.
- No excluded infrastructure or unrelated monthly behavior changes.

READY_FOR_IMPLEMENTATION_TASKS
