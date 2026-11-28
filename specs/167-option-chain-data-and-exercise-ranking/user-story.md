# User Story: Option Chain Data and Call Exercise Ranking

## Feature Folder

`167-option-chain-data-and-exercise-ranking`

## Layers / Area

Layers: backend | database | ops
Area: FinancialData

## Priority

P1

## Complexity

L

## Depends On

None (builds on the released TSETMC direct feed of `054` and the instrument dimension of `064`).

## References

- `specs/167-option-chain-data-and-exercise-ranking/design-proposal.md` (approved at Gate A, 2026-11-28)
- `docs/tsetmc-webservice-datasource.md` — operations, flows, polling schedule, admin endpoints
- `docs/domain-glossary.md` — new terms added by this feature (see task 7)
- Data model: `FinancialIngestionDbContext`; `TradingInstrumentRow`, `LatestMarketQuoteRow`, `StockMarketSyncStateRow`
- Code: `TsetmcWebServiceClient`, `TsetmcWebServiceModels`, `TsetmcDirectFeedSyncService`, `TsetmcPollingWorker`, `PersistedMarketDataProvider`
- Related specs: `specs/054-stockmarketdb-to-tsetmc-direct-feed-migration/`, `specs/064-trading-instrument-unification/user-story.md`, `specs/058-live-data-sync-monitor/user-story.md`
- Open questions: OQ-168 to OQ-171 in `specs/open-questions.md`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As the owner of Sapio's market data,
I want every listed option contract (call and put) stored with its terms, open interest and
current order book, and a ranking of the calls of one underlying by the cost of buying the share
through exercise,
so that the best call to buy and exercise at expiry can be found for a symbol such as اخابر.

## Business Context

Option trade prices already arrive from TSETMC flow 3, but the contracts are stored as unnamed
stub instruments and cannot be found by symbol. Strike, expiry, underlying, open interest and the
order book are not ingested at all. This feature closes that gap for calls and puts and adds the
first decision aid on top of it. Put ranking and any user-facing screen or assistant answer are
later work.

## Scope

- Identity of option contracts in `TradingInstruments` (symbol, ISIN, active flag).
- Contract terms and open interest from the `Option` operation, for calls and puts.
- Latest five order-book levels per option contract from `BestLimitsAllIns` flow 3.
- Scheduled polling, admin-triggered sync, sync-state reporting.
- Option chain query for one underlying and expiry.
- Call exercise ranking for one underlying.
- Read API for the chain and the ranking.

## Out Of Scope

- Put exercise ranking (→ `specs/backlog.md`, phase 4 of the design).
- Web page, Telegram message or assistant capability for options (OQ-169).
- Black–Scholes price, implied volatility, writer margin (`AFactor`, `BFactor`, `CFactor` are not stored).
- Order-book history (OQ-170).
- Futures contracts in flow 3; order books of non-option instruments.
- Exchange and exercise fees in the ranking (OQ-168).

## Functional Requirements

1. **Instrument identity.** After a daily-trade sync, an option contract whose instrument row has
   a numeric stub symbol carries the provider's symbol. Flow 3 is added to `InstrumentFlows` so
   ISIN, name and validity are filled too. Because that response is about 66 MB and 95% of its
   rows are invalid (expired) instruments, only rows with `Valid = 1` are kept from flow 3 and its
   raw payload is not stored.
2. **Stub backfill.** A restartable, idempotent admin operation renames existing numeric stubs
   from the stored raw daily payloads and reports how many rows it changed and skipped.
3. **Contract terms.** For every listed call or put in `Option`, the system stores one option
   contract: side, underlying instrument, strike price, expiry date, begin date, contract size.
   Side comes from the ISIN prefix (`IRO9`, `IROA` call; `IROF`, `IROB` put), which matches the
   symbol prefix ض/ط on every contract checked. Any other row in `Option` (embedded put options
   and similar, symbols starting with ه or ظ, ISIN `IRS…`) is not stored and is counted as
   ignored in the sync result.
4. **Open interest.** Each `Option` sync updates the contract's open positions and the previous
   day's open positions, with the observation time.
5. **Order book.** Each successful order-book sync replaces the whole stored book: levels (1 to
   5) of every option contract in the response are written, and a stored level the response no
   longer contains is deleted. An empty response is a provider failure and changes nothing.
   Stored per level: bid price, bid volume, bid order count, ask price, ask
   volume, ask order count, provider time. Instruments that are not option contracts are ignored.
   A price or volume of zero means "no order on that side" and is stored as absent.
6. **Validation.** A row is rejected, counted and logged when the strike is not positive, the
   expiry is before the begin date, the contract size is not positive, or a level has an ask
   below its bid while both are present. A contract whose own instrument or underlying is not in
   the instrument catalog yet is not rejected and not stored: it is counted as unresolved and
   stored by a later run. The option sync never creates instruments, so `TradingInstruments`
   keeps its single writer (spec 064).
7. **Expiry.** A contract past its expiry date is excluded from the chain and the ranking. Its
   rows are kept.
8. **Option chain.** Given an underlying symbol and optionally an expiry date, the query returns
   the available expiry dates, the underlying's last price with its time, and per strike the call
   and the put, each with symbol, last and closing price, best bid and ask with volumes, open
   interest and contract size. Without an expiry it uses the nearest unexpired one.
9. **Call exercise ranking.** Given an underlying symbol, an optional expiry and a quantity in
   contracts (default 1), the ranking returns the eligible calls ordered by ascending effective
   purchase cost per share, where:
   - effective purchase cost = strike + best ask price;
   - premium over spot = (effective purchase cost − underlying last price) ÷ underlying last price;
   - a call is eligible when its level-1 ask exists, the level-1 ask volume is at least the
     requested quantity, and the last successful order-book sync is not older than the trading
     session of the underlying's latest quote (so the ranking still answers on a closed day,
     from the last session's book).
   Calls that are not eligible are returned separately with the reason (no ask, insufficient ask
   volume, stale quote). Ties are broken by higher ask volume, then by symbol.
10. **Freshness.** Every chain and ranking response states the observation time of the order
    book, of open interest and of the underlying price.
11. **Equity views unaffected.** Scanners, market views and symbol search return the same
    instruments as before this feature; option contracts do not appear in them.

## Data / Schema

`FinancialIngestionDbContext`, one migration.

| Table (new) | Key | Columns | Notes |
|---|---|---|---|
| `OptionContracts` | `Id`; unique `TradingInstrumentId` | `UnderlyingInstrumentId`, `Side` (`Call` or `Put`), `StrikePrice`, `ExpiryDate`, `BeginDate`, `ContractSize`, `OpenInterest`, `PreviousOpenInterest`, `OpenInterestObservedAt`, `FirstSeenAt`, `LastSynchronizedAt` | Index on (`UnderlyingInstrumentId`, `ExpiryDate`, `StrikePrice`). Both instrument columns reference `TradingInstruments`. |
| `OptionOrderBookLevels` | (`TradingInstrumentId`, `Level`) | `BidPrice`, `BidVolume`, `BidOrderCount`, `AskPrice`, `AskVolume`, `AskOrderCount`, `ProviderTime`, `ObservedAt` | Latest state only; bounded by live contracts × 5. |

Existing tables: `TradingInstruments` rows are updated (symbol, and ISIN/name/validity if flow 3
is added); `StockMarketSyncStates` gains `Tsetmc_OptionContracts` and `Tsetmc_OptionOrderBook`.
No column is added to an existing table.

## API (if any)

Routes follow the existing market-data controller conventions; final names are fixed in task 5.

| Method | Route | Request | Response | Notes |
|---|---|---|---|---|
| GET | `/api/v1/market/options/chain` | `underlying` (symbol), `expiry` (optional date) | Requirement 8 | Authenticated user. 404 when the symbol has no option contracts. |
| GET | `/api/v1/market/options/call-exercise-ranking` | `underlying`, `expiry` (optional), `quantity` (optional, ≥ 1) | Requirement 9 | Authenticated user. |
| POST | `/api/v1/admin/tsetmc/optioncontracts/sync`, `/api/v1/admin/tsetmc/optionorderbook/sync` | none | fetched, persisted, unchanged, rejected, unresolved, removed, ignored, duration | Admin only. Same route shape as the existing datasets, served by a separate controller. 503 when the provider fails. |
| POST | `/api/v1/admin/tsetmc/instrument-stubs/backfill` | none | changed and skipped counts | Admin only; requirement 2. |

## AI Query Path (if any)

None in this feature (OQ-169).

## UI (if any)

None in this feature (OQ-169). The new datasets appear in the existing live data sync monitor.

## Non-Functional Requirements

- **Outbound calls.** `Option` and `BestLimitsAllIns` use the existing TSETMC timeout and retry
  settings. An HTML or non-SOAP response (the provider's 403 page) is a provider failure, not an
  empty result, and must never clear stored data.
- **Isolation.** A failed option sync does not stop or delay trade and index polling.
- **Cadence.** Contract terms and open interest: once before the session and then on a
  configurable interval (default 300 s). Order book: configurable interval (default 60 s) inside
  the existing intraday window. Both have their own enable switch, default off.
- **Payload size.** The order-book response (about 2.5 MB) is parsed as a stream and its raw
  payload is not stored.
- **Write volume.** Order-book rows are written only when a level changed. Because unchanged
  rows keep their time, the freshness of the book is the watermark of its sync state, which
  advances on every successful sync.
- **Observability.** Per sync: fetched, persisted, rejected, unchanged counts and duration in logs
  and in the sync result; sync-state rows updated on start and end.
- **Authorization.** Read endpoints require an authenticated user; sync and backfill require the
  admin role, like the existing TSETMC admin endpoints.
- **Layering.** Provider field names stay in the TSETMC adapter. The ranking rule lives in the
  domain or application layer and is unit-tested without database or network.

## Acceptance Criteria

- [x] AC-01: With a fake provider returning a daily trade with symbol `ضمخا1043` for an instrument stored as a numeric stub, the instrument's symbol becomes `ضمخا1043` after the daily sync.
- [x] AC-02: The stub backfill renames stubs from stored raw daily payloads, changes nothing on a second run, and reports both counts.
- [x] AC-03: An `Option` sync with a fake provider stores side, underlying, strike, expiry, begin date, contract size and open interest for a call and for a put; a second identical sync creates no new row.
- [x] AC-04: Rows that break a validation rule of requirement 6 are not stored and are counted as rejected; the other rows of the same response are stored.
- [x] AC-05: An order-book sync stores five levels for an option contract, ignores instruments that are not option contracts, stores a zero price or volume as absent, and writes nothing when the next response is identical.
- [~] AC-06: When the provider returns an HTML page or fails, the stored contracts and order book are unchanged, the sync reports failure, and intraday trade polling in the same worker tick still runs. (Tested: HTML page with 403 and with 200 is a provider failure; stored data and watermark unchanged. Not tested automatically: the worker loop itself; isolation there is by construction — option datasets run after trades and indices, each in its own scope with its own catch.)
- [x] AC-07: The chain query for an underlying returns expiry dates, the underlying price, and per strike the call and put with the fields of requirement 8; an expired contract is absent.
- [x] AC-08: The ranking orders eligible calls by strike + best ask and reports premium over spot. With the screenshot figures (underlying 1,489; asks 730 at strike 800 and 178 at strike 1,400) it returns 1,530 (2.8%) before 1,578 (6.0%).
- [x] AC-09: A call with no ask, with ask volume below the requested quantity, or with an order book from an earlier trading day is listed as not eligible with the matching reason.
- [x] AC-10: Chain and ranking responses carry the three observation times of requirement 10.
- [~] AC-11: Scanner, market-view and symbol-search results are identical before and after option contracts gain names (test with option contracts present in the fixture). (Market pulse was the one reader that takes every active instrument; it now leaves option contracts out, tested on PostgreSQL. Scanners, market summary, insights, trackers and symbol search reach instruments through a company link or a company symbol, which an option contract never has; checked by reading the code, no new test.)
- [x] AC-12: The new datasets appear in the sync-state query used by the live data sync monitor.
- [ ] AC-13 (live, owner or production host): one sync of each new dataset against TSETMC succeeds, and the coverage query shows, for the current session, the number of unexpired contracts, how many have a side, a named instrument, and a level-1 order-book row.

## Assumptions

- Gate A approved the design as written, including: latest-only order book (OQ-170), ingestion and API only for the first delivery (OQ-169), and the ranking measure of requirement 9 without fees or annualization (OQ-168). The owner has not answered these three questions separately; they are confirmed or changed at Gate B.
- Level 1 of the order book is the best price on each side.

## Deviations From The Approved Plan (2026-11-28)

- Contracts that are not listed calls or puts are ignored, not stored without a side (requirement 3). Storing them needed their instruments, which the flow-3 filter leaves out, and they would have been reported as unresolved on every run.
- The option sync does not create instrument stubs (requirement 6); unresolved contracts wait for the instruments sync.
- The order-book sync replaces the whole book and deletes levels that disappeared (requirement 5); otherwise a withdrawn offer would stay stored.
- Staleness is measured against the underlying's latest trading session, not the calendar day (requirement 9).
- The option admin sync routes live in their own controller with their own response (API table).
- Flow 3 of `Instrument` is limited to valid listed option contracts, not all valid rows, so about 1,100 other derivative instruments are not added to the catalog.
- Rejected rows are reported in the sync result and the per-run summary log line; the sample of rejected codes is logged at debug level, because the provider repeats the same bad row on every call.
- Task 5.4 needed no code: the owner decided on 2026-11-28 that the client does not retry on the provider's other address (OQ-171).

## Provider Verification (live, 2026-11-28, one call per operation)

| Check | Result |
|---|---|
| `Option` | 1,867 contracts, 0.9 MB, 89 underlyings, no expired contract, expiries 2026-11-28 to 2028-05-10. `BuyOP` equals `SellOP` on every row. One row has a non-positive strike. Contract size 1,000 on 1,762 rows. |
| Side | ISIN `IRO9`/`IROA` with symbol ض: 897 calls. ISIN `IROF`/`IROB` with symbol ط: 897 puts. No disagreement. 73 other rows (symbols ه, ظ; ISIN `IRS…`) are not listed calls or puts. |
| `Instrument(Flow = 3)` | 64,693 rows, 66 MB, 8 s. 3,130 valid. Contains all 1,867 `Option` contracts (1,840 valid) with symbol, name and ISIN, plus non-option instruments. Underlying instruments are not in flow 3. |
| `BestLimitsAllIns(Flow = 3)` | 8,877 rows, 2.6 MB, 0.4 s. 1,557 of 1,867 contracts have at least one level (1,371 have five); 923 have a level-1 ask. 342 instruments in the response are not option contracts. No level-1 ask below its bid. |
| `TradeLastDay(Flow = 3)` | 644 rows; 633 are option contracts. |
| Host | `service.tsetmc.com` resolves to two addresses. On 2026-11-28 `94.182.113.112` answered HTTP 403 to every request and `46.102.143.212` answered 200 (OQ-171). |
