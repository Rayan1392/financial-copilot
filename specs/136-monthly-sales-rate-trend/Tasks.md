# Feature 136 — Implementation Tasks

These are five bounded implementation slices. Tests belong with the slice
that owns the behavior; there is no independent test-architecture slice.
No task changes the frozen V1 path.

## 1. Source, accepted revision, and identity-safe product read

Update the normalized ingestion/persistence path and the product read model.

- Replace ProductCode-only line-item collapse for this source with the
  source-row contract from Design.md.
- Preserve provider row identity when supplied, provider ProductCode and
  ProductId provenance, canonical row fingerprint, source multiplicity, and
  accepted revision reference.
- Ensure same-code economic rows survive and reordered replay has the same
  canonical multiset; do not use array position as a cross-period ProductKey.
- Add the smallest accepted-revision/current-pointer persistence change that
  permits same-ID corrections and multiple provider revisions for one logical
  period.
- Persist provider revision/publication evidence and receipt time, and make
  acceptance deterministic under replay, corrected same ID, corrected new ID,
  late older report, and concurrent arrival.
- Remove null/legacy OutputType compatibility from this feature's read path.
  The repository predicate is ProductSales and OutputType 0 only.
- Make product resolution company-scoped, provenance-first, guarded for
  natural title-plus-unit fallback, and typed as Resolved, NotFound, or
  AmbiguousProduct with bounded candidates.
- Make product and company readers use the same accepted revision.
- Add focused normalization, persistence, revision, identity, strict-source,
  and replay tests alongside the changed code.

## 2. Product-trend domain, period, calculation, and typed result

Add the narrow product-trend domain behavior using the accepted product read
path.

- Add the product-trend query/use-case focus while reusing the existing
  monthly product comparison query conventions and product normalization.
- Implement product-bounded default selection: latest accepted qualifying
  month with a valid observation for the resolved product, up to the prior
  11 fiscal positions, with explicit gaps.
- Honor explicit year/month/range requests without fabricating product rows.
- Aggregate only after accepted-revision selection, deduplication, product
  identity resolution, and compatible-unit validation; never aggregate
  different ProductKeys merely because their units match.
- Calculate checked decimal rate as product sales value times 100000m divided
  by product sale quantity.
- Implement MissingQuantity, ZeroQuantity, MissingValue,
  InvalidNegativeInput, Overflow, ValidZeroRate, and ValidRate with no
  NaN/Infinity.
- Keep decimal API values at canonical precision and apply display rounding
  only at the presentation boundary.
- Define MonthlyProductTrendResult with company/product identity, title, unit,
  period, production quantity, sale quantity, sales value, calculated rate,
  statuses, resolution state, and bounded candidates.
- Add focused period, aggregation, arithmetic, zero-value, invalid-input,
  overflow, and result-contract tests.

## 3. V2 routing, API, conversation transport, and compatibility

Wire the product trend through the actual V2 architecture.

- Extend MonthlyProductComparisonIntentRules and
  MonthlyProductComparisonQuery/semantic frame to carry a validated product
  slot and product-trend focus; do not use first-token product identity.
- Register the narrowly named MonthlyProductTrend capability and invoke its
  use case from the existing product-aware workflow branch.
- Implement deterministic precedence:
  ProductRevenueMix for share/mix, MonthlyProductComparison for explicit
  comparison/change/driver, MonthlyProductTrend for product trend/rate, and
  MonthlyActivityTrend for exact company-only trend.
- Ensure a product request cannot fall back to a company trend when product
  resolution is NotFound or Ambiguous.
- Add the typed result to workflow messages/result branches,
  ConversationContracts/payload discriminator and versioning,
  backward decoding, AiFacade contracts, and controller mapping.
- Add the result to the persisted response shape without parsing assistant
  text; old persisted payloads must continue to decode.
- Update direct natural-language routing for the representative Persian
  examples in Design.md and preserve existing comparison/company behavior.
- Add routing, typed transport, API mapping, persistence round-trip, and
  backward-compatibility tests.

## 4. Product chart, frontend view model, export, and renderers

Render the typed result consistently in every supported client.

- Add frontend types and mappings in chat.functions.ts,
  AssistantChatBlock/QueryResponse, mapAssistantBlock, and
  mapPersistedMessage.
- Add the product-trend card/view model/chart to message-list.tsx.
- Render product sales-value bars and the product sales-rate line with
  independent value/rate axes and explicit units.
- Keep production and sale quantities in typed tooltip/detail content only.
- Do not add the orange company average to the product chart.
- Leave the existing company chart as company sales bars plus its existing
  orange 12-month average, with no product rate.
- Add browser image export and TelegramAssistantResponseRenderer support
  using the same typed data and product chart contract.
- Render missing/invalid points as status/gaps, never fabricated zeroes.
- Add focused frontend/export/Telegram and company-rate-regression tests.

## 5. Verification, regression, and handoff

Run the complete feature verification and document the result.

- Run the source-row, accepted-revision, strict-source, identity,
  ambiguity, period, aggregation, decimal, and status tests.
- Run V2 routing tests for company trend, product trend/rate,
  ProductRevenueMix, and MonthlyProductComparison precedence.
- Run API/conversation persistence and backward-decoding tests.
- Run browser chart/export, Telegram rendering, and company-chart regression
  tests.
- Confirm MonthlyAverageSalesRate is not read, mapped, exposed, rendered,
  used as fallback, or used to populate product results.
- Confirm no V1 source files, unrelated features, or legacy null migration
  are changed.
- Confirm the acceptance criteria in UserStory.md and the verification
  cases in Design.md are observable and passing.
- Record any provider-evidence limitation as a typed pending/ambiguous
  revision outcome, never as last-write-wins behavior.

