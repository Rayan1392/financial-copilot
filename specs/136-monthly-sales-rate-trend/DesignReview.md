# Feature 136 Design Review

## Review scope

This is a design-only review of:

- UserStory.md
- Design.md
- Tasks.md

No production code and no existing Feature 136 specification file was changed
by this review. The review used the current normalized Noavaran ingestion path,
Feature 129 product-comparison contracts and tests, the active V2 workflow, the
company trend calculator/query path, and the existing web/Telegram contracts.

## Finding summary

| ID | Severity | Area |
|---|---|---|
| B-01 | BLOCKER | Same-code row loss before product calculation |
| B-02 | BLOCKER | No accepted report/revision rule for corrections and late payloads |
| M-01 | MAJOR | Product identity provenance and fallback scope are not implementable as written |
| M-02 | MAJOR | Legacy-null output type wording conflicts with the actual product read contract |
| M-03 | MAJOR | Direct product routing is not wired to the current semantic/query components |
| M-04 | MAJOR | Product trend period default is left unresolved |
| M-05 | MAJOR | Existing company snapshot still calculates and stores a company-level rate |
| M-06 | MAJOR | Product result transport and renderer impact map is incomplete |
| N-01 | MINOR | Decimal overflow and serialized precision are not fully specified |
| N-02 | MINOR | Zero sales-value semantics are too broad |
| N-03 | MINOR | Acceptance criteria are over-counted and partly non-testable |
| N-04 | MINOR | Task slices are more granular than the feature needs |
| NOTE-01 | NOTE | Chart boundary is sound but should explicitly exclude the company average from the product view |

Counts:

- Blockers: 2
- Majors: 6
- Minors: 4
- Notes: 1
- Acceptance criteria reviewed: 37
- Implementation slices reviewed: 17

## Findings

### B-01 — Same-code rows can disappear before Feature 136 sees them

- File/section: Design.md Sections 3, 5, 6, and 7; UserStory.md Aggregation
  rules; Tasks.md Slices 2, 6, and 7.
- Problem: The current normalizer groups normalized items by LineItemCode and
  keeps only the last item. A vendor product code becomes a line code such as
  PRODUCT:<code>, and the database also enforces uniqueness on
  MonthlyReportId plus ProductCode. Two legitimate economic rows that share a
  vendor code are therefore not available for the Feature 136 calculator to
  aggregate. The no-code fallback includes the payload array index, so row
  reordering can create a different identity.
- Why it matters: The design promises that same-product fragments are summed
  before calculating the rate, but the current source can silently drop one of
  those fragments. The resulting productSaleValue, productSaleAmount, and
  rate can all be wrong. This is the exact failure mode the domain correction
  was intended to prevent.
- Required correction: Define the source-row discriminator and distinguish
  exact replay/revision duplicates from distinct product rows that happen to
  share a code. Preserve distinct economic rows or explicitly prove they are
  duplicates before last-write-wins. Do not use array position as a lasting
  cross-period identity. Add a fixture with two same-code rows that must both
  survive, plus an exact-replay fixture that must collapse.

### B-02 — Correction and revision selection is undefined

- File/section: Design.md Sections 4, 7.1, and 12; UserStory.md Aggregation
  rules and Acceptance Criteria 15 and 34; Tasks.md Slices 4, 7, and 17.
- Problem: The design says corrected normalized reports will not be counted
  twice, but the current ingestion behavior does not provide that guarantee.
  For the same ExternalReportId, normalization updates the report and deletes
  all existing line items before inserting new GUIDs. For a different
  activity ID, the logical-period uniqueness constraint can reject the new
  report rather than select it as the accepted correction. The product read
  repository then filters ProductSales and OutputType 0 by period, without an
  accepted-report pointer or revision rule.
- Why it matters: A corrected report may either replace current facts, fail to
  replace them, or leave a future implementation unsure which report is
  authoritative. A late older payload can also overwrite facts when its
  identity collides. Product rates and company totals can become stale or
  non-reproducible.
- Required correction: Specify one authoritative report-selection contract:
  revision identity, provider publication/period/receipt precedence, behavior
  when publication dates are absent, late older payload rejection, concurrent
  ingestion behavior, and the accepted report used by product queries. Preserve
  immutable source evidence or a revision reference if normalized children are
  deleted. Add tests for replay, amendment with the same activity ID, amendment
  with a new activity ID, late older payload, and concurrent arrival.

### M-01 — Product identity is not available in the persisted shape as the design assumes

- File/section: Design.md Sections 3, 5, and 6; UserStory.md Product identity
  and matching; Tasks.md Slice 2.
- Problem: The provider model has ProductId and ProductCode, but the normalized
  line item stores ProductCode as the generated LineItemCode. The persisted
  value does not preserve whether that code came from a provider code, a
  positive ProductId, or the generated natural-key fallback. In the no-code
  case, the fallback includes title, unit, category, instrument, and array
  index. ProductId equal to zero is present in the repository’s live-shape
  fixtures, but there is no evidence in the repository proving product-ID
  stability across months or whether it is globally scoped.
- Why it matters: The acceptance criterion that a stable code survives a
  title rename cannot be safely implemented from the current observation
  shape unless provider-code provenance is known. A generated natural key may
  split a product after row reorder, while a reused provider code could merge
  different products. The phrase period/history window is also not a defined
  lookup scope.
- Required correction: Define ProductKey from the actual persisted provenance.
  Either preserve raw provider identity/provenance and verify its stability and
  scope, or explicitly limit historical continuity to the same normalized
  provider code and never claim more. Mark PRODUCT:NATURAL identities as
  non-canonical across periods unless a separate deterministic source
  discriminator exists. Define fallback uniqueness over the resolved company
  and an explicit period range, including the behavior when two identical
  title/unit rows exist.

### M-02 — Legacy-null output type handling conflicts with the existing product read path

- File/section: Design.md Section 4; UserStory.md Existing implementation facts;
  Tasks.md Slice 4.
- Problem: Design.md says the product path should select output type 0
  including a legacy-null compatibility case. The existing
  EfCoreMonthlyProductComparisonRepository filters exactly ReportType
  ProductSales and OutputType 0. Feature 129 Design-v9 also makes that exact
  filter normative. A null OutputType is a legacy shape and is not equivalent
  to a validated single-month record merely because the report type is
  ProductSales. outputTypeTitle is stored but is not used by the current read
  contract.
- Why it matters: Relaxing the filter could admit legacy, cumulative, or
  semantically unknown rows and double-count a product. Keeping the strict
  filter makes the stated legacy-null compatibility false. The implementation
  cannot choose safely from the current wording.
- Required correction: Choose one rule before implementation. The safer rule
  is strict OutputType 0 for the product trend, with legacy-null rows excluded
  unless a separately verified migration maps them to single-month semantics.
  If legacy rows must be supported, define the exact evidence and mapping
  rule, test outputTypeTitle and all five output types, and state which source
  wins when both mapped legacy and type-0 rows exist.

### M-03 — Direct product routing is not mapped to the current semantic components

- File/section: Design.md Section 9; UserStory.md Routing and drill-down;
  Tasks.md Slice 9.
- Problem: The current V2 workflow has a Feature 129
  MonthlyProductComparisonIntentRules gate and use case, but its BuildQuery
  always sets ProductText to null. Its company extractor can select the first
  non-stop token, which is unsafe for a message containing both product and
  company. MonthlyActivityTrendIntentRules extracts a company symbol only and
  has no product slot. The proposed MonthlyTrendQueryFrame is not an existing
  repository contract.
- Why it matters: A direct request such as a product name plus company plus
  rate cannot reach a product-specific use case deterministically by merely
  reusing the current rules. It may be treated as a company trend, a
  comparison request with no product, or an incorrectly resolved company.
- Required correction: Map the design to the concrete existing V2 workflow,
  semantic registry/frame, MonthlyProductComparisonIntentRules,
  MonthlyProductComparisonQuery, and workflow message/result branches. Add a
  validated product slot and focus to the existing product-aware capability or
  define a narrowly named product-trend capability in V2. Define precedence
  against ProductRevenueMix and MonthlyActivityTrend. Keep V1 frozen, but do
  not describe a new abstract frame as if it already exists.

### M-04 — The no-period product trend is not deterministic

- File/section: UserStory.md Deferred product-owner decisions; Design.md
  Sections 9 and 10; Tasks.md Slices 4, 9, and 17.
- Problem: The design defers whether a product chart defaults to the existing
  twelve-month window or to a user-specified period. A direct query example
  has no period. Existing company trend behavior selects the latest persisted
  snapshot and builds twelve fiscal-month positions. Existing Feature 129
  product comparison behavior selects the latest qualifying ProductSales
  period and the immediately preceding available period.
- Why it matters: The same direct query can produce different result shapes,
  periods, and chart labels depending on which existing use case is reused.
  Product resolution over a period/history window is also affected. This is
  not a presentation-only decision.
- Required correction: Resolve the default before implementation. Prefer the
  existing monthly-sales convention for this feature: latest available
  qualifying ProductSales type-0 month as the endpoint, with the same bounded
  twelve-fiscal-month chart window when a trend is requested; explicit
  year/month or period range overrides it. State behavior when only one month
  is available and when the product exists historically but not in the latest
  window.

### M-05 — The existing company snapshot still calculates and stores a company-level rate

- File/section: Design.md Sections 2, 3, and 10; UserStory.md Product decision
  and Company trend behavior.
- Problem: The current CompanyMonthlyActivityTrendSnapshotCalculator sums
  company SalesAmount and SalesQuantity and assigns
  MonthlyAverageSalesRate. The application snapshot contracts also contain
  MonthlyAverageSalesRate. The current query does not expose it, but the
  company snapshot still contains a calculated cross-row rate. Design.md
  currently permits this as a legacy internal field while the review boundary
  asks for no remaining company snapshot rate path.
- Why it matters: A hidden company rate can be reused accidentally by a new
  API mapping, product option query, backfill, or renderer. It also leaves two
  competing meanings for rate in the same monthly activity domain.
- Required correction: Decide whether the legacy field is explicitly outside
  Feature 136 and add a hard non-use/regression contract, or remove/deprecate
  its calculation and persistence in a separate compatible change. At minimum,
  Feature 136 implementation must never populate, map, or read it; company
  chart/API tests must prove its absence; and the design must name this
  existing field as a compatibility hazard rather than treating it as benign.

### M-06 — Product transport and frontend impact are under-specified

- File/section: Design.md Sections 10 and 11; Tasks.md Slices 3, 10, and
  12–14.
- Problem: The current frontend has a company trend type/chart and a separate
  monthly product-comparison card, but no product-trend result, product chart,
  product chart view model, product option action, or product export renderer.
  The current conversation/API path already has explicit typed fields for
  MonthlyProductComparisonResult. The design says a new
  MonthlyProductTrendResult is additive but does not define its exact
  envelope field, conversation payload version/backward decoder, API mapping,
  message-list dispatch, or Telegram result precedence.
- Why it matters: The direct backend capability could be correct while the
  typed result is dropped during persistence/reload or rendered as the wrong
  card. Product options are not required for direct NL, but the product result
  transport is required for the feature.
- Required correction: Add a concrete impact map for
  ConversationContracts, workflow messages, AiFacadeContracts,
  AiFacadeController, chat.functions.ts, message-list.tsx, browser export,
  and TelegramAssistantResponseRenderer. Define a discriminated result or
  explicit nullable field, payload version/backward decoding, and the
  behavior when both a company trend and product result are present. Keep the
  UI option control deferred if desired; it must not be a backend dependency.

### N-01 — Formula is correct, but overflow and serialized precision are incomplete

- File/section: UserStory.md Canonical formula; Design.md Section 7.5;
  Tasks.md Slices 3 and 7.
- Problem: The million-Rial-to-Toman formula and fixture are correct:
  68,209,284 × 100,000 ÷ 109,949 equals approximately
  62,037,202.7030714240238656. Decimal arithmetic and display rounding are
  stated, but checked overflow behavior, the maximum accepted magnitude, and
  the API’s canonical serialized precision are not.
- Why it matters: A large provider value or malformed quantity can throw or
  produce a value that differs between application, JSON, and chart labels.
- Required correction: Specify checked decimal operations and an
  InvalidInput/Overflow status, the canonical API decimal precision, and the
  distinction between serialized canonical value and zero-decimal display
  rounding. Database precision is only required if product results are
  persisted.

### N-02 — Zero sales-value behavior is too broad

- File/section: UserStory.md Aggregation rules; Design.md Section 7.4;
  Tasks.md Slice 8.
- Problem: The design says a missing or zero sales amount does not create a
  rate point. Feature 129 and the current company calculator retain zero
  SalesAmount as valid reported data, while a zero-value product row with a
  valid nonzero quantity could represent a real reported zero rather than a
  header. The design does not distinguish an empty/header row from a valid
  zero-value economic row.
- Why it matters: A valid zero-value row can either make the product rate
  legitimately zero or make the selected product’s value/quantity aggregate
  incomplete. Silently treating every zero as empty changes reported data.
- Required correction: Define zero-value row semantics using the available
  source evidence. At minimum distinguish empty header rows
  (zero value and no quantities) from a valid zero value with quantities.
  Specify whether valid zero value yields zero rate or an explicit
  unavailable status, and test both cases.

### N-03 — The acceptance criteria are not the smallest testable set

- File/section: UserStory.md Acceptance criteria 1–37; Design.md Minimum
  verification set; Tasks.md Slices 15–16.
- Problem: Several criteria are scope statements or implementation-owner
  assertions rather than observable behavior. AC 1 is documentation/process,
  AC 35 repeats non-goals, AC 36 assigns implementation ownership, and AC 37
  asserts a test count. AC 2–5, AC 6–8, AC 10–16, AC 18–24, and AC 27–34
  also contain overlapping boundary, routing, arithmetic, and regression
  coverage.
- Why it matters: A high count obscures the few financial invariants that must
  block release and makes it possible to mark a meta-criterion green while a
  required data behavior remains untested.
- Required correction: Consolidate into a smaller observable set, roughly:
  source/revision selection; identity and ambiguity; same-product aggregation;
  different-product isolation; formula/precision; invalid data; deterministic
  period/routing; company boundary; product DTO; chart/gaps/quantity UX;
  service behavior; transport/reload; export/Telegram; and regression. Keep
  documentation, ownership, and test-count statements in Definition of Done,
  not acceptance criteria. Retain named tests for every financial invariant.

### N-04 — The task decomposition is unnecessarily granular

- File/section: Tasks.md Slices 1–17.
- Problem: Slices 2, 4, 6, 7, and 8 are one backend identity/read/calculation
  workstream split into many thin slices. Slices 3 and 10 are both contract
  transport work. Slice 11 is primarily a regression check, while Slices 15
  and 16 are test lists rather than implementation slices. Slice 17 defers a
  speculative persistence/backfill decision into the release gate.
- Why it matters: The order hides the real dependency: accepted source/revision
  semantics and identity must be stable before any product result is
  published. It also makes a focused feature appear to require a new
  architecture.
- Required correction: Consolidate into four or five slices:
  1) source/revision and identity-safe read/calculation;
  2) deterministic product contracts and V2/direct-query integration;
  3) product chart and typed client transport;
  4) export/Telegram plus optional company discovery;
  5) focused/regression verification. Make persistence/backfill an explicit
  out-of-scope performance follow-up unless measurements require it.

### NOTE-01 — Explicitly exclude the company orange average from the product view

- File/section: Design.md Section 11; UserStory.md Product chart behavior.
- Problem: The product chart describes bars and a rate line and says no
  average-rate line is added, but it does not explicitly say that the orange
  company sales-value average is absent from the product chart.
- Why it matters: A renderer maintainer could carry the existing company
  average into the new product chart, creating a third visually similar
  reference series with a different scope.
- Required correction: State explicitly that the product chart contains only
  product sales-value bars and the product rate line, unless a separate,
  product-scoped sales-value average is later approved.

## Domain correctness

The central boundary is correct: sale rate belongs to one resolved product,
different products must never be combined, and the formula uses million Rial
sales value plus product sales quantity. The fixture arithmetic is correct and
the provider rate is correctly treated as non-authoritative.

The domain is not yet safe to implement because the persisted source can lose
same-code rows and does not define which corrected report is authoritative.
Those are financial correctness issues, not merely implementation details.

## Architecture fit

The proposed query-time read path fits the repository better than a new
generic analytics subsystem. Feature 129 already provides:

- company resolution;
- ProductSales and OutputType 0 filtering;
- product observations and evidence;
- deterministic normalization;
- a V2 product-comparison route;
- conversation/API/Telegram typed-result patterns.

However, Feature 136 currently describes an abstract semantic frame and
product calculator without mapping them to the actual
MonthlyProductComparisonQuery, intent rules, workflow branches, and response
contracts. The product trend result also requires a new feature-specific
frontend chart and transport path. That is acceptable scope, but it must be
named explicitly.

## Data semantics

Output type 0 is the correct starting source, and output types 1–4 are
persisted separately. The current strict Feature 129 repository filter is
appropriate for a product trend. The legacy-null wording is not safe until
mapped to an unambiguous single-month semantic.

The major unresolved data issue is report/line revision handling. Current
normalization is current-state replacement, not immutable revision lineage:
children are deleted and recreated, while a new activity identity can collide
with logical-period uniqueness. Feature 136 cannot claim duplicate/correction
safety without adding an accepted-source rule or narrowing its source
contract.

## Product identity

Company scoping is correctly required, and Feature 129’s normalization is a
useful starting point. The repository evidence does not establish ProductId
stability or global scope, and normalized persistence does not retain raw
provider identity provenance. The generated natural key’s array index is not a
safe cross-period identity. Identity continuity across title renames therefore
cannot be an unconditional acceptance criterion.

Exact title, partial title, Persian/Arabic normalization, title aliases, and
historical-only products need a deterministic resolution matrix. Fuzzy search
is not required, but the current fallback period/history scope is too vague.

## Chart semantics

The intended product chart is coherent:

- sales value as bars in billion Toman;
- calculated product rate as a line in Toman per product unit;
- independent axes;
- quantity facts in tooltip/detail rather than extra axes;
- null gaps and labeled valid rate points;
- no product-rate average.

The company chart boundary is also coherent in the visible API/chart path.
The product view must explicitly omit the company orange average. Web Canvas
export and Telegram are separate consumers and need their own feature-specific
product rendering paths; the current repository does not already provide them.

## Acceptance criteria quality

All 37 criteria were reviewed. The set covers the important concepts but is
larger than necessary and mixes:

- observable behavior;
- documentation/process requirements;
- implementation ownership;
- scope prohibitions;
- test-count assertions.

The release gate should prioritize the source/revision, identity, aggregation,
formula, period/routing, company boundary, typed transport, and rendering
invariants. Those invariants must be individually named and tested; meta
criteria should move to Definition of Done.

## Task decomposition

All 17 slices were reviewed. The decomposition is not invalid, but it is
over-granular for the feature and gives speculative persistence/backfill and
UI option work too much architectural weight. The first publishable slice
must close source revision and identity safety before the chart or API is
built. UI product options can remain deferred because direct natural language
does not depend on them.

## Deferred decisions

| Decision | Classification | Review |
|---|---|---|
| Product-option UI shape | SAFE_TO_DEFER | Direct natural-language product queries can provide the backend capability without an interactive option control. |
| Product-option maximum/order | SAFE_TO_DEFER, with a bounded server default | It affects presentation and payload size, not the calculation contract. |
| Localized ambiguity/no-data copy | SAFE_TO_DEFER | Stable status codes and deterministic candidate behavior are the required implementation contract. |
| Ambiguous title behavior: request unit/code or list safe candidates | MUST_RESOLVE_BEFORE_IMPLEMENTATION | It changes the response shape, resolver behavior, and acceptance tests. |
| No-period/default trend window | MUST_RESOLVE_BEFORE_IMPLEMENTATION | It changes data retrieval, chart shape, historical identity scope, and direct-query behavior. |
| Query-time versus persisted product snapshot | SAFE_TO_DEFER initially | The existing read repository supports a provider-free query path; performance can be measured first. |
| Decimal database precision/scale | SAFE_TO_DEFER if no product snapshot is persisted; MUST_RESOLVE before persistence | Canonical API decimal precision and overflow status must still be fixed before implementation. |

## Scope assessment

The feature does not inherently require generic product analytics, generic
drill-down infrastructure, fuzzy search, a product master, a chart-framework
rewrite, a monthly-report rewrite, or a semantic-router rewrite.

It does require a narrowly scoped product trend query/result, concrete V2
slot/routing integration, source/revision safety, and feature-specific product
renderers. Those are within scope because they are necessary to deliver the
requested direct product query and typed product chart.

## Final verdict

NEEDS_CHANGES

The domain correction is sound and the formula is correct, but implementation
should not begin until B-01 and B-02 are resolved. M-01 through M-05 must also
be resolved in the design or explicitly narrowed into safe contracts before
acceptance criteria and tasks are finalized.

FEATURE_136_DESIGN_REVIEW_COMPLETE
