# Fix: Instrument Symbol Is The Persian Trading Symbol (BUG-055)

Status: Gate A and Gate B: Approved by the owner on 2026-11-28 in one message («این باگ رو فیکس کن», after the cause, the proposed correction and the production sample were on the table) — implemented 2026-11-28 on `develop` (`a3ae4633`), not released

## Feature Folder

`064-trading-instrument-unification/changes/fix-instrument-symbol-persian`

## Layers / Area

backend · FinancialData

## Defect

`TsetmcDirectFeedSyncService.PersistInstrumentsAsync` wrote the provider's Latin mnemonic
(`CValMne`, for example `MKBT1`) into `TradingInstruments.Symbol` on every instruments sync. The
symbol of an instrument is its Persian trading symbol (`LVal18AFC`, for example «اخابر»): the
daily trade feed writes that value and every reader matches on it.

The equity flows (1, 2, 4) joined the instruments sync on 2026-11-24 (`d1f6fd90`). Since then the
nightly sync has replaced equity symbols with mnemonics.

Evidence:

- Live `Instrument(Flow = 1)` call, 2026-11-28: all 1,736 valid `IRO1` rows have a Latin `CValMne`.
- Production sample supplied by the owner, 2026-11-28: `PSIR4`, `TOKA3`, `AMIN1`, `SDST1`, … in
  `Symbol` next to Persian names and `IRO1…` ISINs.

Affected readers (match on the instrument symbol): the ticker fallbacks in
`PersistedMarketDataProvider` and `MarketViewServices`, fund-portfolio name matching, and any
output that shows the instrument symbol. Company-linked paths were not affected. The size of the
user-visible effect on production was not measured.

## Correction (no design change)

The instruments sync stores `LVal18AFC` as the symbol. The mnemonic is used only when the provider
row has no Persian symbol and the stored symbol is empty or a numeric stub. A stored real symbol is
never replaced by the mnemonic.

No schema change and no data migration: the sync is an upsert over every row the provider returns
(valid and invalid), so the first instruments sync after deployment rewrites the stored symbols.

## Tasks

- [x] 1. `PersistInstrumentsAsync`: symbol from `SymbolFull`; mnemonic only as last fallback.
- [x] 2. Regression test `TsetmcInstrumentSyncTests.InstrumentSymbolIsThePersianTradingSymbol_NotTheLatinMnemonic`: a row already holding a mnemonic is repaired, a Persian symbol survives a provider row without one, a stub falls back to the mnemonic, a new row gets the Persian symbol, a repeat sync changes nothing.
- [x] 3. `docs/tsetmc-webservice-datasource.md` and `specs/bugs.md` updated.
- [ ] 4. After deployment (owner or production host): run `POST /api/v1/admin/tsetmc/instruments/sync` (or wait for the nightly sync), then check that the query in BUG-055 returns Persian symbols and that the sync reported no rejected instruments. An instrument the sync rejects for an identity conflict keeps its old symbol.

## Verification (2026-11-28)

- `TsetmcInstrumentSyncTests` (4 tests) and `OptionMarket167PostgreSqlTests` (2 tests) passed.
- Unit tests `OptionMarket167Tests`, `TradingInstrumentUnificationTests`, `TsetmcWebServiceClientTests`, `StockMarketDbSyncServiceTests`: 78 passed.
- Full backend suite not rerun after this one-method change.

## Risks

- Symbols change back from Latin to Persian on production with the first sync; anything that
  cached or stored the Latin value in between (none known) would differ.
- Instruments from flows 5, 6 and 7 (commodity and energy) also switch from mnemonic to Persian
  symbol. They had mnemonics since they were first synchronized.
