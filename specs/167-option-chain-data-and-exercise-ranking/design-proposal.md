# Option Chain Data and Exercise Ranking — Design Proposal

**Status: Gate A approved by the owner on 2026-11-28.** Gate B (implementation plan in `tasks.md`) not approved. Drafted 2026-11-28.

## Layers / Area

backend, database, ops · FinancialData (ingestion first; AI or frontend exposure is an open question)

## Goal

For an underlying symbol such as اخابر, hold enough option data to find the best call option to
buy with the intent of exercising it at expiry. Put options are ingested from the start; put
ranking is a later phase.

The owner requires, per contract: strike price, expiry date, bid and ask price, bid and ask
volume. Both calls (ض…) and puts (ط…) are ingested.

## Discovery findings

Evidence is from the local database (last local poll 2026-11-18) and from live calls on
2026-11-28. Production was not inspected.

1. **Intraday trade prices of options already arrive.** `TradeLastDay` flow 3 returned 593 rows
   in the last local poll; 550 were option contracts, 10 of them ضمخا contracts. They are written
   to `IntradayTradeSnapshots` and `LatestMarketQuotes`.
2. **The contracts have no identity.** All 555 option contracts traded that day are stub rows in
   `TradingInstruments`: `Symbol` is the numeric `InsCode`, no ISIN, no kind, `IsActive = false`.
   - `InstrumentFlows` is `[5, 6, 7, 1, 2, 4]`; flow 3 is not synchronized.
   - `TradeLastDay` carries no symbol. `TradeOneDay` does (for example `ضمخا1043`), but
     `EnsureInstrumentStub` never updates an existing stub.
   - The table holds 4,125 numeric-symbol stubs in total.
3. **`TradeLastDay` has trade data only**: last, closing, previous close, volume, value, trade
   count. No order book, open interest, strike, expiry or underlying.

## Provider operations (TsePublicV2)

| Operation | Gives | Verified |
|---|---|---|
| `Option(UserName, Password)` | `InsCode`, `InstrumentID`, `BuyOP`, `SellOP`, `ContractSize`, `StrikePrice`, `UAInsCode` (underlying), `BeginDate`, `EndDate`, `AFactor`, `BFactor`, `CFactor`, `YesterdayOP`, `Delta`, `MarketSameRiskOP` | Schema and sample rows supplied by the owner 2026-11-28. Not reproduced by the agent (HTTP 403, see risks). |
| `BestLimitsAllIns(UserName, Password, Flow)` | Five order-book levels per instrument: `number`, `InsCode`, `QTitMeDem`, `ZOrdMeDem`, `PMeDem` (bid volume, order count, price), `PMeOf`, `ZOrdMeOf`, `QTitMeOf` (ask price, order count, volume), `HEven` | Live call, flow 3, 2026-11-28: 8,857 rows, 2.5 MB. Coverage of option contracts not yet cross-checked. |
| `TradeLastDay(Flow = 3)` | Intraday trade snapshot | Already ingested. |
| `Instrument(Flow = 3)` | Symbol, name, ISIN, kind, validity | Not verified (HTTP 403). |
| `BestLimitOneIns(InsCode)` | Order book for one instrument | Listed in the WSDL; not needed if the all-instrument call covers options. |

`Option` has no call/put flag. Verified 2026-11-28: the ISIN prefix (`IRO9`/`IROA` call,
`IROF`/`IROB` put) agrees with the symbol prefix on every listed contract; the adapter uses the
ISIN. The "Verified" column above is superseded by `user-story.md`, "Provider Verification".

## Proposed design

### Phase 1 — Ingestion (calls and puts)

1. **Instrument identity.** Add flow 3 to `InstrumentFlows` if `Instrument(Flow = 3)` returns the
   contracts. Independently, let the daily-trade path replace a numeric stub symbol with the real
   symbol. One idempotent backfill of existing stubs from the stored raw daily payloads.
2. **Option contract terms.** New aggregate `OptionContract`, keyed by the trading instrument:
   side (call/put), underlying instrument, strike price, expiry date, begin date, contract size.
   Written from `Option`. Terms are immutable after first sight except for provider corrections,
   which are logged.
3. **Open interest.** Latest open positions per contract (`BuyOP`, `YesterdayOP`) from the same
   `Option` call, refreshed during the session.
4. **Order book.** Latest five levels per option contract from `BestLimitsAllIns(Flow = 3)`. Only
   the latest state is kept (upsert); no history in this phase, so the table is bounded by the
   number of live contracts times five.
5. **Translation at the boundary.** Provider field names (`PMeDem`, `QTitMeOf`, `UAInsCode`, …)
   stay in `TsetmcWebServiceClient` and its models. Dates arrive as `yyyyMMdd` integers and are
   converted there.
6. **Scheduling.** Same market-hours window as the existing intraday poll. Contract terms once
   before the session and on first sight of an unknown contract; open interest and order book on
   an interval of their own (default proposed: 60 s, configurable), each with the existing
   timeout and retry budget. A failed order-book poll must not block trade polling.
7. **Validation.** Reject rows with a non-positive strike, an expiry before the begin date, an
   unknown underlying, or an ask below the bid; count and log rejections. Expired contracts are
   marked inactive, not deleted.
8. **Observability.** Sync state rows for the new datasets, shown in the existing live data sync
   monitor (Feature 058), with fetched, persisted and rejected counts.

### Phase 2 — Option chain read model

A query for one underlying and expiry returning, per strike, the call and the put with last
price, closing price, best bid and ask with volumes, open interest, and the underlying's last
price. This mirrors the owner's screenshot minus the Black–Scholes column.

### Phase 3 — Call exercise ranking

A domain policy that ranks the calls of one underlying. Proposed measure, to be confirmed
(OQ-168):

- effective purchase cost per share = strike + best ask;
- premium over spot = (strike + best ask − underlying last price) ÷ underlying last price;
- a contract is eligible only if the ask side has volume for the requested quantity (default one
  contract) and the quote is from the current session.

Worked example from the screenshot (اخابر last 1,489), using last trade price in place of the
ask: ضمخا9019 costs 800 + 730 = 1,530 (2.8% over spot); ضمخا9025 costs 1,400 + 178 = 1,578
(6.0% over spot).

### Phase 4 — Put ranking (later)

Mirror of phase 3 for selling the underlying through exercise: effective sale price =
strike − best ask of the put.

## Out of scope for this proposal

Black–Scholes or implied-volatility calculation, margin calculation for writers, order placement,
order-book history, futures contracts in flow 3.

## Risks

- **HTTP 403 from the provider.** On 2026-11-28 the first live call succeeded and the next four
  (two operations, one retry each) returned an HTML 403 page. The cause is unknown: rate limit,
  IP rule or time-of-day rule. Polling cadence and the three unverified points depend on it.
- **Payload size.** `BestLimitsAllIns` flow 3 is 2.5 MB per call. At a 60 s interval that is
  about 0.5 GB per session over the network; raw payload storage for this dataset must be
  bounded or skipped.
- **Options leaking into equity views.** Once roughly 550 contracts have names and are active,
  scanners and market views must exclude them by instrument kind explicitly.
- **Call/put classification** rests on a symbol prefix until the ISIN pattern is confirmed.
- **Thin order books.** Many contracts have an empty ask side; ranking must report "no executable
  offer" instead of ranking on stale last prices.

## Alternatives considered

- **Parse strike and expiry from the instrument name.** Rejected: `Option` supplies them as typed
  fields with the underlying's `InsCode`.
- **`BestLimitOneIns` per contract.** Rejected as default: about 550 calls per poll.
- **Store order-book history.** Deferred: not needed for ranking and grows without bound.

## Open questions

OQ-168 to OQ-171 in `specs/open-questions.md`.

## References

- `docs/tsetmc-webservice-datasource.md`
- `specs/054-stockmarketdb-to-tsetmc-direct-feed-migration/`
- `specs/064-trading-instrument-unification/user-story.md`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Tsetmc/TsetmcDirectFeedSyncService.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/Tsetmc/TsetmcWebServiceClient.cs`
