# Feature 136 — Monthly Product Production, Sales, and Sales-Rate Trend

## Status

Revision after DesignReview.md: implementation-ready specification.

This feature adds a product-specific monthly trend to the existing V2 monthly
activity/product capability. It does not change the frozen V1 path and does
not expose the legacy company-level average sales-rate field.

## User story

As a financial-market user, I want to ask for the monthly production,
sales quantity, sales value, and sales rate of a named product for a named
company, so that I can see the product trend without confusing it with the
company's total sales or with another product that happens to use the same
unit.

Examples:

- روند فروش ماهانه کچاد → company monthly sales trend.
- روند فروش آهن اسفنجی کچاد → product monthly trend.
- نرخ فروش آهن اسفنجی کچاد → product monthly rate trend.
- سهم آهن اسفنجی از فروش کچاد → ProductRevenueMix.

## Scope and non-goals

In scope:

- V2 natural-language routing for a product plus company.
- Strict monthly ProductSales observations with OutputType equal to 0.
- Deterministic accepted-report/revision selection.
- Identity-safe product resolution and same-code row preservation.
- Production quantity, sale quantity, sales value, and calculated sales rate.
- Typed Resolved, NotFound, and Ambiguous outcomes.
- A web and Telegram product trend result rendered as value bars and rate line.
- Replay, revision, arithmetic, routing, transport, and regression verification.

Out of scope:

- Any new V1 intent, route, parser, or response branch.
- Company-wide sales rate, weighted rate across products, or use of the
  company MonthlyAverageSalesRate field.
- Product discovery screens, product selectors, aliases, fuzzy matching, or
  title-renamed product continuity without provider evidence.
- Null OutputType compatibility, OutputType-title inference, or a migration
  of legacy null rows.
- Persistence of a product snapshot unless implementation evidence shows that
  the existing accepted-report read path cannot satisfy the contract.

## Authoritative source contract

Only rows from an accepted report satisfying all of the following are
eligible:

- ReportType equals ProductSales.
- OutputType equals 0, the SingleMonth output type.
- The report belongs to the requested company and fiscal month.
- The report is the deterministic accepted revision for its logical report
  period.

Rows with null OutputType, any other OutputType, service reports, report-title
inference, or legacy compatibility behavior are not eligible for this
feature. A separate verified migration would be required to change that rule.

The logical report key is Provider, ExternalCompanyId, fiscal period,
ReportType, and OutputType. All consumers of this feature use the same
accepted report revision. Product totals and company totals must never read
different revisions.

An exact replay of an accepted revision is idempotent. A correction is a new
accepted revision only when provider revision evidence makes it newer under
the deterministic rule in Design.md. A late report with older provider
publication/revision evidence never replaces a newer accepted report.
Concurrent arrivals use an atomic compare-and-swap/current-pointer operation;
database arrival order is not the business rule.

## Product identity and resolution

Product identity is company-scoped.

The preferred ProductKey is a persisted provider ProductCode with its
provenance. A positive provider ProductId may be used only after its
stability and scope are proven by the provider contract and persisted as
provenance. If no stable provider identity exists, a guarded natural key of
normalized product title plus normalized unit may be used only within the
same company and requested trend window. A natural key is not evidence of
continuity through a title rename.

Two candidates that satisfy a natural-language product request are not
silently merged or selected. Resolution returns:

- Resolved: one stable product identity.
- NotFound: no qualifying product identity/observation.
- Ambiguous: more than one candidate.

An Ambiguous result contains a bounded candidate list with display title,
unit, and stable provider key/code when available. The UI may present these
options later, but product discovery UI is not required for V1 of this
feature. Direct natural-language product requests are required.

The source-row contract must preserve distinct economic rows that share a
provider product code. Array position is evidence for replay diagnostics
only; it is never a product identity across periods. Exact replay rows
collapse idempotently, while legitimate same-code rows remain separately
represented until same-product aggregation is explicitly and safely applied.

## Trend period

For a product request with no explicit period, the endpoint is the latest
accepted qualifying ProductSales/OutputType-0 month in which the resolved
product has a valid observation. The default contains that month and up to
the preceding 11 fiscal-month positions, for a maximum of 12 positions.

The window is product-bounded, not company-bounded. If the product has no
observation in a company-wide latest month, that month is not substituted for
the product endpoint. Missing fiscal months are returned as explicit gaps,
not as zeroes and not as another product. One available month is one data
point; fewer than 12 available months remain fewer than 12 observations with
explicit gap positions. An explicit year, month, or range overrides this
default.

## Calculations and value semantics

After accepted-report selection, row deduplication, and product identity
resolution, only rows with the same ProductKey and compatible unit may be
aggregated. Different ProductKeys never aggregate merely because their unit
is the same.

For a surviving product observation:

    SalesRate = ProductSalesValue × 100000 / ProductSaleQuantity

The calculation uses checked decimal arithmetic. A zero sales value with a
positive quantity is a valid zero rate. A zero quantity does not produce a
rate. Empty/header rows are not economic rows when both value and meaningful
quantity are absent. Provider sentinel values are invalid according to their
documented semantics.

Each observation has a typed status: MissingQuantity, ZeroQuantity,
MissingValue, InvalidNegativeInput, Overflow, ValidZeroRate, or ValidRate.
Invalid observations have no NaN or Infinity representation and cannot
silently become zero. The API preserves decimal values at canonical contract
precision; display rounding is separate and follows the established monetary
display convention.

## Chart contract

The existing company chart remains unchanged: monthly company sales value
bars plus the existing orange 12-month company average. It has no product
sales-rate line.

The product chart contains exactly:

- sales-value bars in billion Toman;
- a sales-rate line in Toman per product unit on an independent rate axis;
- valid labels only for valid rates.

The product chart has no orange company-average line. Production quantity
and sale quantity are typed fields shown in tooltip/detail content; they do
not introduce additional axes.

## Transport and rendering contract

The result is a typed MonthlyProductTrendResult, not text parsed from an
assistant answer. It contains company identity, product identity, product
title, unit, fiscal period, production quantity, sale quantity, sales value,
calculated rate, and observation status. It also contains the typed
resolution outcome and bounded candidates when the outcome is Ambiguous.

The result is carried through the actual V2 workflow messages and result
branches, conversation contracts/payload versioning and backward decoding,
AiFacade contracts/controller mapping, frontend chat mapping and message
rendering, browser image export, and TelegramAssistantResponseRenderer.
Older persisted messages without the new discriminator continue to decode
unchanged.

## Acceptance criteria

AC-01. A product trend request is handled only through the V2 product-aware
   capability; no V1 code or new V1 route is added.
AC-02. An eligible observation is read only from an accepted report where
   ReportType is ProductSales and OutputType is exactly 0; null and other
   output types are excluded.
AC-03. The accepted report is deterministic for each logical report key, is
   idempotent for an exact replay, and never lets an older late revision
   replace a newer accepted revision.
AC-04. Concurrent report arrivals converge on one accepted revision, and the
   product and company calculations use that same revision.
AC-05. Two legitimate source rows sharing one provider product code both survive
   normalization and persistence; an exact replay does not double-count them;
   reordering the replay produces the same economic result.
AC-06. Product resolution is company-scoped and uses provider identity provenance
   where available; a guarded title-plus-unit fallback does not assert title
   rename continuity.
AC-07. Product resolution returns Resolved, NotFound, or Ambiguous. Ambiguous
   resolution never auto-selects and returns bounded title/unit/key
   candidates.
AC-08. Without an explicit period, the product-bounded window ends at the latest
   accepted month with a valid observation for the resolved product and has
   at most 12 fiscal-month positions with explicit gaps.
AC-09. An explicit year/month/range is honored; one month and fewer than 12
   months remain valid bounded results without fabricated zero rows.
AC-10. Only rows with the same resolved ProductKey and compatible unit aggregate;
    same units do not merge different products.
AC-11. Product sales rate uses checked decimal arithmetic with the specified
    100000 multiplier, canonical decimal API output, separate display
    rounding, and typed overflow/invalid statuses.
AC-12. A positive quantity with zero sales value returns ValidZeroRate and rate
    zero; missing/zero quantity, missing value, negative input, and overflow
    return their typed statuses and never NaN or Infinity.
AC-13. The result exposes company identity, product identity/title/unit, period,
    production quantity, sale quantity, sales value, calculated rate, status,
    and typed resolution metadata without parsing assistant text.
AC-14. The routing precedence distinguishes company trend, product trend/rate,
    ProductRevenueMix, and two-period MonthlyProductComparison as specified;
    the four example requests route to the expected capability.
AC-15. The company chart has company sales bars and its existing orange average
    only; the product chart has product sales-value bars and product-rate
    line only, with quantities in detail/tooltip.
AC-16. The typed result is mapped through workflow, conversation payload,
    AiFacade/controller, frontend browser, export, and Telegram paths; old
    payloads still decode.
AC-17. The feature does not read, map, expose, render, or use
    MonthlyAverageSalesRate and does not populate a product result from it.
AC-18. Product discovery UI is not required for the direct request, and no
    fuzzy matching, aliasing, or silent candidate choice is introduced.
## Definition of done

All AC-01 through AC-18 pass in automated tests and focused manual checks.

Focused automated verification covers normalization, replay, revision,
routing, transport, calculation, rendering, and regression cases, including
absence of company-level rate in product results.

Implementation is documented in the five bounded work slices in Tasks.md,
and DesignReview.md remains unchanged.

The implementation records source-row and accepted-revision provenance
needed to reproduce a result. API, browser, export, and Telegram contracts
agree on the discriminated result. No production change is made outside the
approved five slices, and no stale legacy behavior is described as supported.

The implementation evidence is produced with no unresolved blocker or major
finding.

