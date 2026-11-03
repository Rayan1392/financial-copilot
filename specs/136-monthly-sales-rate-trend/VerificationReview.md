# Feature 136 — Final Verification Review

## Review scope

This is a read-only final verification of the revised Feature 136
specification against DesignReview.md and the repository architecture. No
existing file was modified and no production code was implemented.

Reviewed:

- UserStory.md
- Design.md
- Tasks.md
- DesignReview.md
- the existing ingestion, monthly-trend, V2 workflow, conversation,
  frontend, export, and Telegram code points named by the specification

## Original blockers

### B-01 — Same-code row loss: RESOLVED

The revised contract defines source-row identity separately from product
identity in Design.md, Source-row identity and normalization.

It now specifies:

- provider row identity when supplied;
- provider ProductCode/ProductId provenance;
- canonical economic-row fingerprint;
- report-local multiplicity/occurrence evidence;
- preservation of distinct same-code rows;
- exact accepted-revision replay idempotency;
- canonical sorting and reorder-independent economic results; and
- no array position or generated line-item code as a cross-period ProductKey.

UserStory.md acceptance criteria 5 and 10 and Design.md verification cases
2–6 and 19–20 make the behavior observable. The required removal/replacement
of ProductCode-only GroupBy behavior is included in Tasks.md slice 1.

### B-02 — Correction/revision selection: RESOLVED

Design.md, Source filter and accepted report model, defines:

- a logical report key;
- immutable candidate revisions;
- an accepted/current revision pointer;
- exact replay no-op behavior;
- same-provider-ID corrections;
- different-provider-ID corrections;
- late older payload rejection;
- deterministic provider-evidence ordering;
- concurrent compare-and-swap/current-pointer handling; and
- the same accepted revision for company and product reads.

The rule does not use database arrival order as the business rule and does
not permit last-write-wins when provider ordering evidence is absent.
UserStory.md acceptance criteria 3–4 and Design.md verification cases 7–12
cover the required cases. Tasks.md slice 1 includes the necessary persistence
change.

## Original major findings

### M-01 — Product identity provenance: RESOLVED

Contract: provider ProductCode with provenance is preferred; positive
ProductId is usable only after stability and scope are proven; otherwise
company-scoped normalized title plus unit is a bounded fallback without
title-rename continuity.

Evidence: UserStory.md, Product identity and resolution; Design.md,
Product identity and resolver; Tasks.md slice 1.

Duplicate candidates return typed Ambiguous/AmbiguousProduct with bounded
title, unit, and stable provider-key candidates. No arbitrary selection is
allowed.

### M-02 — OutputType handling: RESOLVED

Contract: only ReportType = ProductSales and OutputType = 0 qualify.
Null OutputType, OutputTypes 1–4, service reports, and title-based output
type inference are excluded.

Evidence: UserStory.md, Authoritative source contract; Design.md, Strict
eligibility; Tasks.md slice 1.

### M-03 — Direct V2 product routing: RESOLVED

Contract: the design extends real existing V2 components rather than
inventing a generic router:

- MonthlyProductComparisonIntentRules;
- MonthlyProductComparisonQuery;
- the semantic registry/frame;
- MonthlyActivityTrendIntentRules;
- ProductRevenueMix;
- the existing product/company resolver logic; and
- FinancialCopilotWorkflowDefinition.

The narrowed MonthlyProductTrend capability carries a validated product slot
and emits MonthlyProductTrendResult. Product extraction is not first-token
selection. A product request cannot silently fall back to company trend.

The revised precedence is deterministic:

1. ProductRevenueMix for share/mix/contribution;
2. MonthlyProductComparison for explicit comparison/change/driver;
3. MonthlyProductTrend for a validated product trend/rate request; and
4. MonthlyActivityTrend for an exact company-only monthly trend.

Evidence: Design.md, V2 routing and precedence; UserStory.md acceptance
criteria 1, 14, and 16; Tasks.md slice 3. The four required examples map to
the expected capabilities.

### M-04 — Default product trend period: RESOLVED

Contract: without an explicit period, use the latest accepted qualifying
ProductSales/OutputType-0 month containing a valid observation for the
resolved product, then include up to the preceding 11 fiscal-month positions.
Gaps remain explicit. One month and fewer than 12 months are valid results.
Explicit year/month/range requests override the default.

Evidence: UserStory.md, Trend period and acceptance criteria 8–9; Design.md,
Period selection; Tasks.md slice 2.

There is no conflicting deferred period decision in Tasks.md.

### M-05 — Legacy company MonthlyAverageSalesRate: RESOLVED

Contract: Feature 136 must not read, map, expose, render, use as fallback,
or use MonthlyAverageSalesRate to populate a product result. Historical
cleanup is explicitly outside scope.

Evidence: UserStory.md scope/non-goals, acceptance criterion 17, and chart
contract; Design.md, Legacy company-rate hazard; Tasks.md slices 2, 4, and 5.

### M-06 — Product transport/rendering path: RESOLVED

Contract: MonthlyProductTrendResult is a typed discriminated result carried
through V2 workflow messages, ConversationContracts and payload versioning,
backward decoding, AiFacade/controller mapping, persistence/reload,
frontend dispatch, product chart/view model, browser export, and Telegram
rendering. Assistant text is never parsed to reconstruct the result.

Evidence: UserStory.md, Transport and rendering contract and acceptance
criteria 13 and 16; Design.md, Typed result and transport and Chart and
export design; Tasks.md slices 3–4; Design.md verification case 24.

The persistence/reload verification explicitly protects against silently
losing the product result in conversation replay.

## Domain invariants

### A. Company-level trend — PASS

The existing company trend remains the company sales aggregation/chart path,
with monthly company sales bars and no product sale-rate series. The revised
product path does not replace or enrich it with a rate.

### B. Product-level isolation — PASS

The product trend operates on one resolved company-scoped ProductKey.
Different ProductKeys never aggregate for rate, including products with the
same unit.

### C. Same-product aggregation order — PASS

Design.md requires accepted-revision selection, source-row deduplication,
product resolution, and unit compatibility before same-product aggregation.

### D. Formula — PASS

The specified calculation is:

    SalesRate = ProductSalesValue * 100000m / ProductSaleQuantity

This is the requested SalesValueMillionRial-to-Toman-per-unit formula, with
checked decimal arithmetic.

### E. Provider productSaleRate authority — PASS

The provider rate is retained only as source evidence/fingerprint input.
The authoritative result is the checked calculated rate defined above; the
typed result exposes calculated sales rate.

### F. ServiceSales semantics — PASS

The Feature 136 source contract admits only ProductSales with OutputType 0.
ServiceSales cannot enter the product-trend read path, so no manufactured
quantity or product-rate semantics are assigned to service rows.

## Source contract — PASS

The contract is exactly ReportType = ProductSales and OutputType = 0.
Null OutputType and OutputTypes 1–4 are excluded. outputTypeTitle is not used
to infer monthly semantics.

## Product identity — PASS

Provider provenance/stable identity is preferred. Natural title/unit fallback
is company- and window-scoped, does not assert title-renamed continuity, and
returns typed bounded ambiguity instead of selecting an arbitrary candidate.

## Period semantics — PASS

The revised UserStory.md, Design.md, and Tasks.md all use the same
product-bounded latest-valid-month plus up to 11 preceding fiscal positions
rule with explicit gaps and explicit-period override.

## V2 routing — PASS

The routing contract uses existing workflow and semantic components and
defines precedence among ProductRevenueMix, MonthlyProductComparison,
MonthlyProductTrend, and MonthlyActivityTrend. The four required examples
map to the expected capabilities.

## Transport/replay — PASS

The result discriminator, versioning, typed workflow/application/API
contracts, persistence/reload behavior, frontend mapping, export, and
Telegram renderer are all named. Backward decoding and persistence
round-trip verification prevent silent result loss.

## Chart semantics — PASS

The product chart has product sales-value bars, a product sale-rate line,
independent value/rate axes, valid rate labels, and typed quantity detail.
It has no company orange average, company rate, or quantity axes in V1.
The company chart remains company sales bars plus its existing orange
12-month average.

## Invalid-data semantics — PASS

Null quantity, zero quantity, null value, valid zero value with positive
quantity, negative invalid input, overflow, and missing fiscal months have
deterministic treatment. Valid zero rate is distinct from unavailable rate,
and NaN/Infinity are prohibited.

## Acceptance-criteria quality — FAIL

Acceptance criteria 1–18 are observable/deterministic feature or regression
behavior. Acceptance criterion 19 requires an automated verification
program, and criterion 20 requires documentation in Tasks.md and preservation
of DesignReview.md. Those are Definition-of-Done/process conditions, not
independently observable product acceptance behavior.

This is a concrete specification-quality defect: move ACs 19–20 to
Definition of done/scope and retain the behavioral invariants in the
verification plan. No domain or architecture redesign is required.

## Task decomposition — PASS

The five slices have a one-way dependency:

1. source/revision/identity-safe read;
2. product-trend domain/application calculation;
3. V2 routing/API/conversation transport;
4. web/export/Telegram rendering; and
5. focused and regression verification.

Slice 1 correctly blocks later financial calculations. There is no circular
dependency and no independent test-architecture slice.

## Scope control — PASS

The revised scope does not require a generic product master, BI drill-down
framework, fuzzy-search subsystem, semantic-router rewrite, chart-framework
replacement, persisted product snapshot in V1, or company-snapshot redesign.

## Final verdict

NEEDS_CHANGES

Reason: all original blockers and majors are resolved, but ACs 19–20 remain
process/documentation metadata inside the acceptance-criteria list and must
be moved to Definition of Done/scope before implementation approval.

FEATURE_136_VERIFICATION_REVIEW_COMPLETE
