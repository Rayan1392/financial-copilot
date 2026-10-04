# Feature 136 — Design

## Design decision

Feature 136 extends the existing V2 product-aware monthly capability with one
narrowly named MonthlyProductTrend focus, query path, and typed result. It
reuses the current semantic registry/frame, the
MonthlyProductComparisonIntentRules entry point and query conventions, the
existing company resolver/product logic, and the existing workflow transport
patterns. It does not introduce a generic router rewrite and does not touch
the frozen V1 path.

The feature has two independent correctness boundaries:

1. accepted source facts and product identity;
2. the V2 routing/result/rendering contract.

Neither boundary may infer missing facts from assistant text or from the
legacy company snapshot.

## Existing code points and intended changes

The implementation must be anchored to these repository components:

- NadpcoApiMonthlyActivityNormalizer, including its current
  CollapseDuplicateLineItems behavior and product payload mapping.
- NormalizedMonthlyReportRow and
  NormalizedMonthlyReportLineItemRow, their EF configuration and indexes.
- MonthlyProductComparisonQuery, ProductSalesObservation,
  MonthlyProductComparisonIntentRules, and the semantic registry/frame.
- MonthlyActivityTrendIntentRules and the existing company resolver.
- MonthlyActivityTrendQueryUseCase, ProductRevenueMix, and the V2
  FinancialCopilotWorkflowDefinition branches.
- ConversationContracts, workflow messages, AiFacade contracts/controller,
  and the persisted assistant payload discriminator/version.
- frontend chat.functions.ts, mapAssistantBlock/mapPersistedMessage,
  message-list.tsx, the existing company trend chart/export path, and
  TelegramAssistantResponseRenderer.

The existing product-comparison path currently does not populate ProductText
in its query builder. Feature 136 must add a validated product slot and must
not rely on its current first-token company extraction as product identity.

## Source filter and accepted report model

### Strict eligibility

The product-trend repository query contains all of these predicates:

- ReportType = ProductSales.
- OutputType = 0.
- requested company.
- requested fiscal period.
- report is the accepted revision for its logical report key.

There is no null OutputType branch, no outputTypeTitle inference, and no
legacy fallback. ProductSales rows with OutputType 1, 2, 3, or 4 are excluded.
Legacy null rows remain out of scope unless a separately verified migration
changes the source contract.

### Logical report and revision identity

The logical report key is:

    Provider + ExternalCompanyId + PeriodStart + PeriodEnd
    + ReportType + OutputType

The persistence model must support more than one immutable candidate revision
for a logical report key. The current unique ExternalReportId and logical
period indexes cannot remain the only constraints because a provider may reuse
an activity/report ID for a correction and several provider IDs may represent
revisions of the same logical period.

The smallest safe persistence change is:

- retain immutable report candidates with Provider,
  ExternalReportId/activity ID, logical report key, SourcePayloadChecksum,
  provider revision metadata, ProviderPublishedAt when available,
  ReceivedAt, and synchronization timestamps;
- add a revision discriminator/fingerprint so an exact replay of the same
  provider ID and checksum is one candidate, while a changed payload with the
  same provider ID can be stored as another candidate;
- replace the unique logical-period-as-row constraint with a unique/current
  accepted-revision pointer for each logical report key; and
- persist AcceptedRevisionId or an equivalent IsAccepted/current pointer with
  optimistic concurrency.

The existing fields may be extended rather than adding a new table if the
same immutable-candidate and current-pointer guarantees are preserved. Raw
provider evidence need not be copied into the result, but enough provenance
must remain to explain why a revision was accepted.

### Deterministic acceptance rule

For every candidate of a logical report key, compare evidence in this order:

1. provider revision sequence/version, if the provider documents it;
2. provider publication date/time, including a persisted time-zone-normalized
   value;
3. provider correction/revision identifier, but only if its ordering is
   documented or explicitly supplied as an ordered sequence;
4. otherwise, no automatic replacement is allowed.

Receipt time is persisted for audit and is a deterministic tie-breaker only
among candidates whose provider evidence is otherwise equal and whose
provider contract permits that tie-break. Receipt time must never make a
known older provider publication replace a newer one.

The cases are therefore deterministic:

- Same provider ID and same checksum: exact replay, no-op.
- Same provider ID with changed payload and later provider revision or
  publication evidence: store the candidate and move the current pointer.
- Same provider ID with changed payload but no newer provider evidence:
  retain the current revision and mark the candidate pending/ambiguous for
  revision handling; never use last-write-wins.
- New provider ID for a correction: compare provider revision/publication
  evidence under the same rule; accept only the newer candidate.
- Late older report: store provenance if useful, but it cannot replace the
  current pointer.
- Concurrent arrivals: serialize the current-pointer compare-and-swap (or
  use an equivalent transaction/advisory lock), re-read the winner, and
  reject a losing stale update.

The product and company monthly calculations both resolve the current
accepted revision before reading line items. They cannot independently pick
the latest database row.

## Source-row identity and normalization

### Required contract

ProductCode is a product identity candidate, not a source-row identity. A
provider line-row identifier, when supplied by the provider, is persisted as
ProviderRowId/SourceRowKey with its provider provenance. A ProductId is not
treated as a row identifier unless the provider explicitly documents that
meaning.

For each accepted report revision, a normalized source row preserves:

- provider row identity when available;
- provider ProductCode and positive ProductId separately, with provenance;
- normalized title, unit, category/instrument evidence;
- production quantity, sale quantity, sales value, and provider rate;
- a canonical row fingerprint;
- source payload checksum/revision reference; and
- an occurrence/multiplicity marker only as report-local replay evidence.

The line-item uniqueness constraint must therefore be based on the accepted
revision plus SourceRowKey, not MonthlyReportId plus ProductCode.

### Exact duplicate versus distinct economic row

The exact duplicate decision is made in this order:

1. same accepted provider revision and same provider row identity is the
   same source row;
2. without provider row identity, the same canonical row fingerprint in the
   same accepted payload is the same fact only to the extent of its source
   multiplicity; and
3. an exact replay of the accepted report is identified by the report
   revision/checksum and is not inserted again.

The canonical row fingerprint includes the provider identity fields when
available and all normalized economic fields that describe the source fact:
product code/ID, title, unit, category/instrument evidence, production
quantity, sale quantity, sales value, provider sales-rate value, and the
source report/revision. Field ordering and JSON property ordering are
canonicalized. The fingerprint is not a ProductKey.

If a payload has two rows with the same code but different provider row IDs,
both survive. If it has two same-code rows with different economic fields,
both survive. If no row IDs exist and identical facts occur multiple times,
their source multiplicity is preserved; their local occurrence ordinal is
evidence for that report only. Array position is never used as a product
identity across periods.

For a replay with rows in a different array order, canonical sorting by
fingerprint and preserved multiplicity produces the same economic multiset,
same totals, and same ProductKey outcomes. No positional ProductCode such as
PRODUCT:code:index may be used.

The current GroupBy(LineItemCode) collapse must be removed or replaced for
this path. The normalizer/persistence change is part of Feature 136, not an
optional future fix.

## Product identity and resolver

Product identity resolution is company-scoped and evidence-first:

1. use a provider ProductCode only when it is persisted with provider and
   company provenance;
2. use a positive provider ProductId only if stability, scope, and
   nonzero semantics are proven by the provider contract and persisted;
3. otherwise use normalized title plus normalized unit only within the same
   company and requested trend window.

Current payloads where ProductId is zero do not satisfy step 2. A generated
line-item code, source array index, EF row ID, or report-local ordinal cannot
become a cross-period ProductKey.

Natural fallback has no title-rename continuity. A renamed title is a new
candidate unless provider identity evidence joins it. If more than one
candidate matches a natural request, return the typed AmbiguousProduct
outcome with a bounded candidate list containing display title, unit, and
stable provider code/key when available. Do not select the first row or ask
the LLM to choose.

The domain result uses these resolution states:

- Resolved: one product identity and observations.
- NotFound: no matching product/observation in the requested scope.
- Ambiguous: multiple candidates; no trend series is calculated.

The presentation may localize the wording, but the typed state and candidate
data are stable across API, web, export, and Telegram.

## Same-product aggregation

Aggregation happens only after accepted-revision selection, source-row
deduplication, company-scoped product resolution, and unit compatibility
validation.

Rows may aggregate only when they have the same resolved ProductKey and
compatible unit. Different ProductKeys never aggregate just because both say
تن. Same provider code rows can remain separate source facts and then sum
only when the resolved provider identity and compatible unit establish that
they are the same product. Conflicting identity/title/unit evidence produces
an ambiguity or invalid observation rather than a silent merge.

The formula is applied to the summed surviving product sales value and summed
surviving product sale quantity:

    rate = productSaleValue * 100000m / productSaleQuantity

Use checked decimal arithmetic. The implementation must define a known
maximum magnitude if the API/domain already has one; otherwise decimal
overflow is caught and returned as Overflow. API values remain decimal with
canonical contract precision. Display formatting rounds only at the
presentation boundary using the established monetary convention; a display
rounded value is never fed back into calculation.

## Zero, missing, negative, and overflow semantics

An empty/header row is not an economic observation when it has no meaningful
quantity and no value. A valid economic row with positive sale quantity and
zero sales value is a valid zero-rate observation.

The domain status is one of:

- MissingQuantity: quantity is absent.
- ZeroQuantity: quantity is zero, so rate is undefined.
- MissingValue: value is absent.
- InvalidNegativeInput: a provider quantity/value is negative where the
  provider contract disallows it.
- Overflow: checked calculation exceeds decimal/domain limits.
- ValidZeroRate: positive quantity and zero value.
- ValidRate: positive quantity and valid nonzero rate.

Provider sentinel values are mapped according to documented provider
semantics. No invalid state is represented as NaN or Infinity. Invalid
points remain typed and are not silently plotted as zero.

## Period selection

For a product trend without an explicit period:

1. resolve the product using qualifying accepted ProductSales/OutputType-0
   observations;
2. choose the latest accepted month with a valid observation for that
   product;
3. return that month and up to its preceding 11 fiscal-month positions;
4. retain missing positions as explicit gaps.

This is a product-bounded window. It is not the latest company month with
empty product data substituted. One month returns one point; a product with
fewer than 12 available months returns fewer valid observations plus gap
positions. A product with historical observations but none in the requested
explicit range returns NotFound/NotReported according to the shared domain
contract. Explicit year/month/range parameters override the default.

## V2 routing and precedence

The existing V2 semantic frame and registry gain a validated product slot and
the MonthlyProductTrend focus. Product text is not obtained by taking the
first non-stop token; it is resolved against the company-scoped candidate
evidence before a series is built.

Precedence is:

1. ProductRevenueMix when the request expresses share, portion, mix, or
   contribution of a product to company sales.
2. MonthlyProductComparison when it requests two periods, comparison,
   change, driver, or production-versus-sales comparison.
3. MonthlyProductTrend when a validated product slot is present and the
   request asks for trend, monthly sales/production, or sales rate.
4. MonthlyActivityTrend for an exact company-only monthly activity trend.

A product slot always prevents a generic company trend from consuming the
request. An unresolved product produces the typed NotFound or Ambiguous
result, not a company-only fallback.

Concrete mappings:

| Request | V2 capability |
| --- | --- |
| روند فروش ماهانه کچاد | MonthlyActivityTrend |
| روند فروش آهن اسفنجی کچاد | MonthlyProductTrend |
| نرخ فروش آهن اسفنجی کچاد | MonthlyProductTrend |
| سهم آهن اسفنجی از فروش کچاد | ProductRevenueMix |
| تغییر فروش آهن اسفنجی کچاد بین دو ماه | MonthlyProductComparison |

Implementation-wise, extend MonthlyProductComparisonIntentRules and
MonthlyProductComparisonQuery to carry validated ProductText/product focus
into the existing product-aware branch, register the narrowly named
MonthlyProductTrend capability, and add
IMonthlyProductTrendQueryUseCase.
The workflow maps the trend focus to MonthlyProductTrendResult while leaving
the existing comparison result branch intact. MonthlyActivityTrendIntentRules
must decline a validated product request.

No new V1 intent, parser, route, or V1 response field is allowed.

## Typed result and transport

MonthlyProductTrendResult is a discriminated result with:

- result discriminator monthly_product_trend;
- result/payload version;
- company identity and display name;
- product identity, stable provider key/code when available, title, and unit;
- explicit period positions and fiscal labels;
- production quantity;
- sale quantity;
- sales value;
- calculated sales rate;
- per-point status;
- resolution state and bounded ambiguous candidates;
- evidence/freshness metadata required by existing contracts.

The discriminator/version is carried through:

1. V2 workflow messages and result branches;
2. ConversationContracts and AssistantMessagePayload versioning;
3. backward decoding, where older payloads lacking this discriminator
   continue to decode with a null new field;
4. AiFacadeContracts and AiFacadeController mapping;
5. frontend chat.functions.ts types, QueryResponse,
   AssistantChatBlock, mapAssistantBlock, and mapPersistedMessage;
6. message-list.tsx and a product-trend card/viewmodel/chart;
7. browser image export for the product chart; and
8. TelegramAssistantResponseRenderer and a product-aware chart renderer.

One typed result branch is used end to end. No client parses assistant
prose to reconstruct values, identity, status, or periods. Company identity
and product identity come from the typed result and are not inferred from
the rendered title.

## Chart and export design

The existing company chart remains the company chart: monthly company sales
bars and its existing orange 12-month average. Feature 136 does not add rate
to it.

The new product chart contains only:

- bars for product sales value in billion Toman;
- a line for product sales rate in Toman per product unit;
- independent value and rate axes with explicit units and valid labels.

The orange company average is never rendered on the product chart.
Production and sale quantities are included in tooltip/detail rows and typed
export data, not as extra axes. Missing/invalid statuses are represented as
gaps or status text according to the existing chart conventions, never as
fabricated zeroes.

Browser export and Telegram use the same typed data and chart contract, with
renderer-specific layout only.

## Legacy company-rate hazard

CompanyMonthlyActivityTrendSnapshotCalculator currently calculates and stores
MonthlyAverageSalesRate in the company snapshot. Feature 136 must not read,
map, expose, render, use as fallback, or use to populate any product result.
The product trend query reads accepted product observations directly.

No historical cleanup is required for this feature. Deprecation/removal of
the company snapshot rate is a follow-up design, outside this feature.
Regression tests must prove that the new product result contains no company
rate field/series and that an absent product rate is not filled from the
company snapshot.

## Verification design

The following focused cases are required:

1. Strict ProductSales and OutputType 0 selection excludes null and every
   other output type.
2. Exact replay of one accepted report is idempotent.
3. Same-code rows with distinct provider row IDs both survive.
4. Same-code rows with different economic fields both survive without
   GroupBy(ProductCode) loss.
5. Identical source multiplicity survives when provider row identity is
   unavailable.
6. Reordered replay has identical economic multiset and totals.
7. Same ID correction with later provider evidence becomes current.
8. Same ID changed payload without ordering evidence remains pending and
   does not overwrite current.
9. New ID correction with newer publication evidence becomes current.
10. A late older revision never replaces a newer current pointer.
11. Concurrent candidates converge on one deterministic accepted revision.
12. Product and company readers observe the same accepted revision.
13. Provider ProductCode is company-scoped and retained with provenance.
14. Unverified ProductId/zero ProductId is not used as ProductKey.
15. Natural title-plus-unit fallback is window/company scoped and does not
    bridge a title rename.
16. Duplicate natural candidates return AmbiguousProduct with bounded
    candidates and no selected series.
17. Default product endpoint is latest valid product observation and the
    window contains at most 12 fiscal positions with explicit gaps.
18. Explicit period overrides the default; one/fewer-than-12 periods do not
    receive fabricated rows.
19. Different ProductKeys with equal units never aggregate.
20. Same ProductKey with compatible units aggregates only after deduplication.
21. Positive quantity and zero value produce ValidZeroRate and decimal zero.
22. Missing/zero quantity, missing value, negative input, and overflow map
    to the specified statuses without NaN/Infinity.
23. Routing maps all five representative requests to the stated capabilities.
24. Typed result round-trips through workflow, API, persistence, frontend,
    browser export, and Telegram; older payloads still decode.
25. Company chart remains unchanged and product chart excludes company
    average while showing product value/rate and quantity detail.
26. Company MonthlyAverageSalesRate cannot appear in product data or chart.

## Non-functional constraints

- Keep all source evidence needed to explain accepted revision and product
  identity decisions.
- Make replay and concurrency deterministic.
- Use decimal for API/domain financial values.
- Keep ambiguity explicit and bounded.
- Do not introduce fuzzy matching, LLM identity decisions, or text parsing.
- Preserve existing company trend and comparison behavior outside the new
  product-trend focus.

