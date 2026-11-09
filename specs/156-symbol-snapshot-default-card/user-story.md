# User Story: Symbol Snapshot Default Card

## Feature Folder

`156-symbol-snapshot-default-card`

## Layers / Area

Layers: backend | frontend | ops
Area: AI | Conversations | FinancialData

## Priority

P1

## Complexity

M

## Depends On

None

## References

- `specs/156-symbol-snapshot-default-card/design-proposal.md` (approved at Gate A; §13 lists planning clarifications)
- `specs/155-admin-unanswered-question-demand-report/design.md` §5, demand theme T06
- `specs/POLICY-V1-FREEZE.md`
- `docs/domain-glossary.md` terms used: company, symbol, monthly report, financial statement
- `docs/architecture.md`
- Data model: `FinancialIngestionDbContext`: `NoavaranEligibleCompanies` view (`NoavaranEligibleCompanyRow`), `Markets`, `IndustryGroups`
- Related specs: `specs/154-ai-balance-sheet-report/`, `specs/141-*` fallback behaviour

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user,
I want to send only a ticker and immediately see its latest price, monthly sales and quarterly report,
so that my first message gives me useful data and a clear next step instead of a refusal.

## Business Context

Sending only a ticker is a natural first message in web chat and Telegram. Today it ends in a clarification or refusal. Feature 155 records this demand as theme T06.

## Scope

- Bare-ticker detection for companies in `NoavaranEligibleCompanies`.
- One Symbol Snapshot turn: latest price, latest monthly sales, latest quarterly report.
- Up to four next-step buttons.
- Web chat, mobile PWA, Telegram, and reopened conversations.
- Feature flag `SymbolSnapshot:Enabled`, default off.

## Out Of Scope

- Company names, fuzzy matches, multiple tickers, or a ticker with other words.
- Price charts, technical analysis, valuation, buy or sell advice.
- New ingestion, new persistence tables, or migrations.
- Turning the flag on in production, releases, tags, and deploys (these follow `docs/release/release-process.md` and need the owner's separate go-ahead).

## Functional Requirements

1. After normalization, removing trailing `؟ ? . ! ،` and an optional leading `نماد`, `سهم` or `سهام`, a message of at most 40 characters is a bare ticker when its compact key (spaces and ZWNJ removed) equals the compact `CompanySymbol` or `TseSymbol` of exactly one eligible company.
2. Detection does not apply when any of these holds: the flag is off; the active conversation task has a `PendingAction`; the request carries an `InsightEventId`; the eligible-symbol lookup fails. In each case the existing flow runs unchanged.
3. A detected turn makes no model call for interpretation.
4. The price section shows latest price, daily change percent and trading date.
5. The monthly section shows the latest reported month, sales amount with its unit, month-over-month change, and year-over-year change.
6. The quarterly section shows the latest standalone fiscal quarter and its audit status:
   - General template: sales (15), gross profit (139), net profit (143), EPS (160).
   - Banking template: net interest income (306), net fee income (307), net profit (143), EPS (160).
   - Year-over-year change per row against the same fiscal quarter of the previous fiscal year, using the normalizer's comparison rule.
7. Each section is independent. A section with no data or a failure shows one muted Persian line and the other sections still appear.
8. Buttons, at most four and in this order: `روند فروش ماهانه {symbol} را نشان بده`, full quarterly report, balance sheet, product revenue mix. A button for a section with no data is dropped. (A disclosures button was planned for a free slot; it was removed in implementation because no wording passes the round-trip check below: the interpreter also reads «اطلاعیه» as a company mention. See `specs/backlog.md`.) Each button's text must round-trip through the interpreter to its capability with the same symbol; the planned wording may change to satisfy this.
9. If all sections are empty, the turn is `NoData`, free of charge, and is not sent to the Feature 141 fallback.
10. In `CyclicalWaves` mode the card shows price and quarterly, with an "unavailable for this data source" line for monthly and no monthly button. Production runs `NoavaranAmin`.
11. A snapshot leaves the company in the conversation state like other capabilities, so a following question can inherit it.

## Data / Schema

Read only: the `NoavaranEligibleCompanies` view (`Id`, `ExternalCompanyId`, `Name`, `CompanySymbol`, `TseSymbol`, `MarketId`, `GroupId`), `Markets`, `IndustryGroups`. No migration.

`DetectedIntent` is stored as an integer in persisted JSON, so `SymbolSnapshot` is appended as the last member.

## API (if any)

| Method | Route | Request | Response | Notes |
|---|---|---|---|---|
| POST | existing AI query endpoints | unchanged | adds nullable `symbolSnapshotResult` | Additive; existing clients are unaffected. Also present in conversation history payloads. |

## AI Query Path (if any)

V2 only. New capability `symbol_snapshot`, route `CapabilityExecutionRoutes.SymbolSnapshot`, required slot `symbol`, excluded from capability guidance. Statuses: `Executed` (all sections), `Partial` (some), `NoData` (none).

## UI (if any)

New `SymbolSnapshotCard`, rendered from `message-list.tsx`, used by desktop and mobile. RTL, with an intro line, a header (symbol, name, market, group, existing `FollowSymbolButton`) and three rows. Each row shows its date or period. Changes have text as well as colour. Telegram uses `TextAnswer` and the existing suggested-action list.

## Non-Functional Requirements

- Sections run one after another with a per-section timeout of 2 s and a turn budget of 4 s, both configurable. Exhausting the budget marks remaining sections unavailable.
- No retries inside the turn.
- The response carries summaries only, with no chart or multi-quarter table payload.
- The eligible-symbol set is cached in memory for 10 minutes. It is bounded by the view's size (about 875 rows locally).
- Logs and traces carry `workflow.intent = SymbolSnapshot`, per-section status and latency, and the correlation id.
- With the flag off, behaviour is identical to today.

## Acceptance Criteria

- [x] AC-01: With the flag on, `اخابر`, `کگهر`, `کگل`, `نماد اخابر` and `اخابر؟` produce a Symbol Snapshot with no refusal.
- [x] AC-02: Arabic `ك`/`ي` and ZWNJ or space variants of a ticker match.
- [x] AC-03: `سلام`, `اخابر سود`, two tickers, and a ticker outside the view do not produce a snapshot and follow today's flow.
- [x] AC-04: A ticker answering a pending "which symbol?" clarification continues that clarification.
- [x] AC-05: With the flag off, routing of a bare ticker is unchanged.
- [x] AC-06: A detected turn does not call the hybrid interpreter.
- [x] AC-07: Card values equal those of the source use cases for the same company, on all three sections.
- [x] AC-08: A bank shows the banking rows, with no gross-profit row.
- [x] AC-09: Year-over-year uses the same fiscal quarter of the previous year; a sign change or non-positive base shows no percent.
- [x] AC-10: Each section failing or timing out alone leaves the other two visible.
- [~] AC-11: All sections empty gives `NoData` and zero credits (tested). The Feature 141 fallback exclusion is in the workflow code but has no automated test.
- [x] AC-12: At least one section with data charges exactly the minimum credit (1).
- [x] AC-13: At most four buttons appear in the specified order, none for a missing section, and each resolves to its capability when pressed.
- [x] AC-14: `CyclicalWaves` mode shows price and quarterly with the unavailable line, and no monthly button.
- [x] AC-15: Telegram shows the three sections as text with suggested actions.
- [x] AC-16: A reopened conversation shows the card and buttons.
- [x] AC-17: The card renders correctly in RTL on desktop and at phone width with no horizontal scroll, and has an accessible section label and row headings.
- [~] AC-18 (not run; the read-only role lacks access to the price, monthly and view tables): A live Development check passes for `اخابر`, `کگهر`, `کگل` and one bank.

## Assumptions

- `CompanySymbol` holds the ticker users type (confirmed by task 1.1: 877 eligible companies, `TseSymbol` identical, `Ticker` empty, no shared keys).
- A ticker equal to a common word, sent alone, means the ticker. Task 1.1 found `بورس`, `دی`, `مهر`, `نماد` and `ما`; the owner's decision is OQ-011.
