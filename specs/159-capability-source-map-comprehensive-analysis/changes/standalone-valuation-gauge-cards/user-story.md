# User story — Standalone valuation gauge cards

Status: Gate A: Approved by the owner on 2026-11-25

Layers: backend, frontend, telegram-gateway, ops. Area: AI, FinancialData, Conversations, Billing.

Dependencies: Feature 148's latest-state tables and sync, Feature 159's source map, the existing canonical market-quote reader, and existing V2 conversation persistence and billing. Feature 115 is a source of presentation primitives.

As a Sapio user reviewing one listed company, I want to ask for its P/E, P/S or equilibrium-price gauge in chat so I can see where the observed ratio or price sits in its reference range and judge the age and source of the facts.

[Approved design](./design-proposal.md) · [Proposed implementation plan](./tasks.md) · [Provider contract](../../../../docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-contract.md)

## Acceptance criteria

| ID | Given / when | Then |
|---|---|---|
| AC-01 | The feature is enabled and the effective source is CyclicalWaves. A user requests a single kind with «گیج P/E شاراک», «گیج P/S کگهر» or «بازهٔ قیمت تعادلی اخابر». | Return the requested kind for the exact resolved ticker. Missing or ambiguous ticker uses the existing clarification/disambiguation path. A resolved ticker with no unique active vendor match returns a coverage miss. |
| AC-02 | A user asks «گیج‌های ارزش‌گذاری شاراک». | Return the available cards in P/E, P/S, equilibrium order, with one short summary of unavailable kinds. With no usable facts, show one ordinary chat response that reports no data, stale data or temporary unavailability as applicable. |
| AC-03 | A ratio gauge has valid counts and bounds. | Show six equal angular sectors with day-count percentages totaling 100% at the chosen precision. Put provider `avg` at 90°, `min` at 30° and `max` at 150°. Use provider status for the position label and preserve the exact observed ratio. Null close hides the needle. Null status remains unranked. Out-of-axis values pin the needle and keep the actual value visible. Zero total omits distribution and invalid geometry falls back to text. |
| AC-04 | A valid equilibrium snapshot is present. | Show signed lower, midpoint and upper bounds in rials. Use the canonical quote, with separate source and timestamp, for the needle. Pin outside the range and give the corresponding text. A missing or timed-out quote keeps the range usable. Degenerate ranges show facts in text. |
| AC-05 | A vendor snapshot exceeds the configured fetch age, initially 48 hours, or a read exceeds its budget. | Preserve status and fetch metadata where known. Suppress expired values in new answers and keep independent usable kinds. Honor caller cancellation. Label fetch time separately from a market timestamp or calculation date. |
| AC-06 | A reply is delivered live or read from an authorized conversation's history. | Web renders the same stored facts and geometry and Telegram renders equivalent text. Older message payloads still load. Feature rollback stops new card generation while keeping existing cards readable. |
| AC-07 | Eligibility, tooling or input changes. | With the flag off apply the current behavior in either base mode. Under NoavaranAmin, the new route additionally needs its CyclicalWaves mapping. Under base CyclicalWaves no override is needed. Card execution uses no LLM, vendor HTTP, cross-provider valuation fallback, or legacy data. |
| AC-08 | A gauge turn ends or is retried. | Planning baseline OQ-131: charge one configured minimum for a usable answer, including an all-gauges turn or range-only answer. Clarification, no-data, stale-only and wholly failed turns are free. The existing ledger settles each request once. |
| AC-09 | A user sends an ordinary ratio lookup, a threshold screen, standalone legacy P/S request with the flag off, a report or peer comparison, or an analysis/fallback phrase. | Existing route, source, presentation and tariff apply. Planning baseline OQ-130: the bare «قیمت تعادلی شاراک» keeps its current route. Monthly sales and its PNG output keep their existing behavior. |
| AC-10 | The card is viewed on a narrow or wide screen in either theme. | All facts remain available as text in reading order. RTL card layout preserves the dial's numeric direction. At 320–390 px and 200% text zoom, labels remain accessible and the card stays within the chat column. Status does not depend on color. |
| AC-11 | An eligible web user opens the symbol menu. | Planning baseline OQ-132: show «گیج‌های ارزش‌گذاری ‹نماد›» using the existing chat action and normal billing flow. Without eligibility, omit the action. |

The API extension is an optional typed response and history field on the existing authenticated chat endpoints. Initial delivery uses existing storage and carries no migration or ingestion changes. Forward needles and historical equilibrium percentages require further data verification. This change covers V2 orchestration only.

Gate B approval includes the design's recommended settings for OQ-130 through OQ-132. The decisions are recorded in [open questions](../../../open-questions.md).
