# Standalone valuation gauge cards

Status: Gate A: Approved by the owner on 2026-11-25

Approval record: «**Gate A**  مورد تایید است» on 2026-11-25. See the [approved implementation plan](./tasks.md) for the Gate B record.

## Scope

Show independent P/E, P/S and equilibrium-price cards in Sapio web chat. A request for one gauge returns one card. An all-gauges request returns the available cards in P/E, P/S, equilibrium order, with a short summary for unavailable sections. Telegram renders the same facts as text. The supplied screenshots guide the semicircular dial, markers and layout. Use Sapio's visual language.

This is the gauge step of Feature 159. Feature 148 stores the latest snapshots. Feature 115 supplies reusable percentage rounding and gauge geometry, and Feature 125 already consumes some of these v2 facts for peer comparisons. The cards have no dependency on the sales chart, peer publications, legacy vendor endpoints, or a change to the base chat provider. Numeric P/E and P/S point lookup and forward ratios are outside the initial scope.

[Provider contract](../../../../docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-contract.md) · [Source selection ADR](../../../../docs/adr/0001-per-capability-chat-data-source.md) · [Feature 148 verification](../../../148-cyclicalwaves-v2-independent-financial-data-provider/ImplementationReport.md)

## V2 routing and provider isolation

| Request | Proposed behavior |
|---|---|
| «گیج P/E شاراک» or «گیج P/S کگهر» | Return one ratio card for the requested symbol. |
| «گیج قیمت تعادلی اخابر» or «بازهٔ قیمت تعادلی اخابر» | Return the equilibrium card for the requested symbol. |
| «گیج‌های ارزش‌گذاری شاراک» | Request all three kinds for one symbol in one turn. |
| «قیمت تعادلی شاراک چقدر است؟» | Use current behavior pending OQ-130. |
| «P/E شاراک چقدر است؟», «سهام با P/E زیر ۵», «شاراک را با صنعتش مقایسه کن» | Use the existing point lookup, scanner, and peer comparison, respectively. |
| Missing or ambiguous ticker in a gauge request | Reuse the clarification or disambiguation outcome. A resolved ticker without vendor coverage is a no-data case. |

Add one dedicated V2 capability, tentatively `valuation_gauges`, with an explicit requested-kind set. Use the current Persian normalization and canonical entity resolution. Restrict initial matching to explicit, single-purpose requests for one symbol. Multi-symbol, threshold, buy/sell and mixed-purpose requests follow current routes. The old Feature 148 matcher is broader. Reuse neither its substring ticker search nor its broad predicates for the new entry point. Use the same validated route for dispatch, availability and billing. Resolve it before operation selection and make explicit equilibrium-gauge intent take precedence over generic valuation fallback.

Extend the allowlist in `ChatCapabilitySourceMap` and use `IChatCapabilityAvailability` as the source authority. Default `ValuationGaugeCards:Enabled` to false. In base `NoavaranAmin` mode, eligibility additionally requires mapping this capability to `CyclicalWaves`. In base `CyclicalWaves` mode it uses the base source. Keep existing startup validation for invalid mappings. With the feature enabled and eligible, dispatch the dedicated route before the existing Feature 148 response for the explicit card phrases. With the feature off, both base modes retain their current routes and answers. V2 uses the existing shared outcome, accounting and persistence stages. The gauge executor is deterministic and needs no model.

## Data, geometry and failure handling

- **Identity and reads.** Resolve the public ticker, then match its active vendor record by canonical identity or unique normalized ISIN. Check catalog membership by requested kind before exposing any stored row. Read only existing Feature 148 snapshots scoped to the resolved symbol and requested kinds. Continue using current synchronization and DataAdmin. No vendor call on the request path and no financial-data schema change are proposed.
- **P/E and P/S.** Carry `close`, `start`, `end`, `min`, `avg`, `max`, six day counts, provider `status` and fetch time. Label `close` «آخرین نسبت ثبت‌شده» and the fetch timestamp «زمان دریافت داده». Use the provider status for the position label. A null status is neutral and can denote missing close or a boundary. With valid boundaries and nonzero counts, use six 30° sectors. Proposed anchors are `start` at 0°, `min` at 30°, `avg` at 90°, `max` at 150° and `end` at 180°. Split `min..avg` and `avg..max` at their midpoints for piecewise visual interpolation. These intermediate edges are display geometry. Validate them against reference examples. Reuse the largest-remainder percentage rounding from Feature 115 so visible percentages sum to 100%. Keep its existing callers on their current geometry. With null `close`, omit the needle. With a zero count total, omit distribution percentages. Invalid or degenerate axes fall back to values in text. If a valid ratio is outside its axis, pin the needle at the edge and retain the actual value in text.
- **Equilibrium.** Read the lower bound, midpoint and upper bound in rials from `EquilibriumRanges`. Read the latest valid quote from the existing canonical quote port. Preserve quote provenance and timestamp independently of the range fetch time. Propose a neutral semicircular dial with 0°, 90° and 180° anchors for lower bound, midpoint and upper bound. Outside the bounds, pin the needle and label «زیر بازهٔ تعادلی» or «بالاتر از بازهٔ تعادلی» alongside the actual price. Missing quote yields a range-only card. Preserve valid negative bounds. Degenerate ranges fall back to text. The current v2 contract lacks the equilibrium distribution, growth rate, actual calculation timestamp and forward ratio values.
- **Freshness and failure.** Propose a 48-hour configurable vendor fetch-age limit for the daily sync, to be validated in staging. For older facts in a new answer, show the age and stale status while suppressing the old values and needle. Use the quote port's market-session policy for the quote, including last trading session on closed-market days. Bound expected read timeouts, retain usable sections on partial failure and propagate request cancellation. A quote timeout can still yield a range card. Use existing exception and telemetry handling for unexpected failures.

## Response, history and tariff

Add an optional `valuationGaugeCards` collection to the V2 response and persisted assistant result. Each requested kind carries a discriminator, state, ticker, source, fetch metadata, exact facts and optional normalized geometry. Expected states include available, partial, no data, stale and temporarily unavailable. Use the existing exact decimal representation in the backend and lossless decimal strings on the wire for displayed values. Keep bounded angles separate from exact facts. Format localized numbers and dates at presentation.

Build fallback text and the Telegram summary from the same typed result. Apply the existing conversation ownership checks. Live and history responses use the same stored snapshot. Disabling the feature stops generation of new cards and history stays readable. Older payloads without this field follow the current rendering behavior.

Pending OQ-131, propose one minimum-credit settlement for a turn with any usable requested card, including range-only. A three-card answer is one turn. Clarifications, all-no-data, stale-only and failed results settle at zero. Reuse the existing ledger and retry protection. Verify operation selection before relying on any existing default tariff.

## Frontend experience

Use Sapio's React chat components, fonts and semantic theme tokens. Apply UI/UX Pro Max guidance for mobile-first hierarchy, contrast, reduced motion and visible textual equivalents for charts. Follow the reference gauge form with Sapio's design. Reuse the current gauge rendering primitives where their behavior fits.

| Element | Proposed design |
|---|---|
| Header | Metric and ticker form the heading. Show a neutral-language status such as «بین میانگین و حد بالا» in text alongside any color. |
| Key facts | Put the latest value and comparison values first. Keep bounds, source and timestamps visible. The quote and equilibrium range have distinct source labels and timestamps. |
| Wide chat column | Put dial and value list side by side inside each card. Stack cards in a consistent order within one response. Use message width so the sidebar does not cause clipping. |
| Narrow chat column | Stack key value, status, dial and facts. Move dense sector labels to a visible list below the dial when they would collide. Keep the scale lower-to-higher within the RTL card. Isolate mixed-script labels and signed values. |
| Partial or missing data | Keep usable facts visible. Use concise localized statuses such as «مقدار فعلی در دسترس نیست» and «دادهٔ گیج به‌روز نیست». If every requested card is unavailable, show one concise normal chat response. |
| Accessibility | Expose all facts in visible text in meaningful reading order. Treat the redundant dial as decorative for assistive technology. Use color plus text or marker shape. Target 4.5:1 for normal text and 3:1 for meaningful graphics. Avoid introductory needle animation. Any disclosure control uses visible keyboard focus and a 44 px minimum touch target. |

Check Persian RTL, English, Sapio light/dark themes, 320–390 px phones, tablet, desktop with the sidebar, and 200% text zoom. Include long ticker labels, signed prices, missing close, stale data and text-only geometry fallback. Collect rendered browser evidence during implementation.

## Proposed implementation sequence

| Order | Work | Verification before advancing |
|---|---|---|
| 1 | Check target-environment coverage using DataAdmin and read-only samples. Finalize wording, tariff and entry points. | Sample all three gauge types, negative bounds, missing close, stale data and a missing or ambiguous vendor match. Verify identity and timestamp semantics. |
| 2 | Implement typed read model and deterministic projection in Application and Infrastructure. Reuse the canonical quote reader and existing percentage rounding where appropriate. | Fixtures cover asymmetric averages, all anchors, zero totals, signed values, exact decimals, nulls, partial availability, quote timeouts and cancellation. |
| 3 | Wire the gated V2 capability, operation selection and persistence. Likely seams are the current capability interpreter/executor, source map, V2 workflow, `MessagePersistenceFunction` and API contracts. | Test both base modes, map/flag combinations, routing priority, conversation ownership, tariff, retry and source isolation. Assert no LLM or vendor HTTP call for card execution. |
| 4 | Implement web cards and Telegram text. Connect optional menu actions through the same explicit chat request. | Verify live/history parity, accessibility, partial data, old payloads and responsive layouts. |
| 5 | Roll out first to a limited group under NoavaranAmin and compare behavior with the flag off and on. | Verify source attribution, timestamp quality, correct charging and rollback. Check that history renders after disabling new generation. |

Regression cases cover point ratios, scanner filters, comprehensive analysis, monthly sales and PNG export, quarterly reports, Feature 163 fallback, Feature 125 peer comparisons and CyclicalWaves-mode Feature 115 behavior. Compare route, source, outcome and ledger effects. Track coverage misses, fetch age, per-kind outcomes, read latency and billing disposition using existing telemetry conventions.

## Confirmed product settings

- **OQ-130** — Require «گیج» or «بازه» for explicit equilibrium-card requests in the first release.
- **OQ-131** — Charge one minimum credit per usable turn and zero for unavailable turns.
- **OQ-132** — Offer typed requests and a symbol-menu shortcut using the existing chat action flow.

The [user story](./user-story.md) records the acceptance criteria. The owner approved the [implementation plan](./tasks.md) at Gate B on 2026-11-25, including the three settings above.
