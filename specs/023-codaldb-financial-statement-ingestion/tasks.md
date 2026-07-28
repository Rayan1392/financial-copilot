# Tasks

## Domain / Semantic — New MetricCode definitions (015 catalog)

- [ ] Add governed `FinancialMetricDefinition` entries (and English + Persian `MetricAlias`es) to
      `PhaseOneFinancialSemanticCatalog` for the new **source** metrics: `REVENUE`,
      `TOTAL_REVENUE`, `GROSS_PROFIT`, `OPERATING_PROFIT`, `EPS`, `EPS_CONSOLIDATED`,
      `FINANCE_COSTS`, `INCOME_TAX`, `TOTAL_EQUITY`, `CAPITAL`. (`NET_PROFIT` already exists —
      reuse it for Codal `Net income`.) Use `MetricUnit.Amount` except `EPS`/`EPS_CONSOLIDATED`
      (`Amount` per share) and set supported period types (3/6/9/12-month).
- [ ] Persian aliases sourced from the CodalDB `ItemTitle` (Persian) values for each mapped item
      so the parser can resolve user terms in either language.

## Infrastructure — Statement Selection & Mapping

- [ ] Add `CodalDbStatementSelectionPolicy` — given the variants for a
      `(CompanyId, PeriodEnd, PeriodType)`, return the canonical one
      (audited > unaudited → latest representment → consolidated/parent by config). Configurable
      consolidated-vs-parent default via `CodalDbProviderOptions`.
- [ ] Add `CodalDbStatementItemMaps` — static governed dictionaries
      `IncomeItemIdToMetricCode` and `BalanceItemIdToMetricCode` per the curated mapping table in
      the user story (verified ItemIds: Revenue 15, Total Revenue 300, Net income 143→NET_PROFIT,
      Operating profit 140, Gross profit 139, Earning per share 160, Net Profit consolidated per
      share 168, Finance costs 12, Income taxes payments 13; Total equity 147, Paid capital 188).

## Infrastructure — Period Mapping

- [ ] Add `CodalDbFiscalPeriodMapper` — maps `(FiscalYearEnd, PeriodEnd, PeriodType)` to
      `(DateOnly PeriodStart, DateOnly PeriodEnd, int PeriodLengthMonths)` using the Gregorian
      columns directly; retains the Jalali strings as evidence. No estimation/`StaleData`.

## Infrastructure — Normalizer

- [ ] Add `…/Ingestion/CodalDb/CodalDbFinancialStatementNormalizer.cs`
      (`ProviderName = "CodalDb"`, `Dataset = FinancialStatements`):
      - Deserialize the statements payload (header + income amounts + balance amounts).
      - Apply `CodalDbStatementSelectionPolicy` per `(CompanyId, PeriodEnd, PeriodType)`.
      - For the selected statement, emit one `IncomeStatement` row + one `BalanceSheet` row,
        sharing the mapped period window, with `ExternalStatementId` suffixed `:INC` / `:BS`.
      - Write line items only for mapped `ItemId`s via `CodalDbStatementItemMaps`.
      - Record selection flags + assumed scale + Jalali dates in `WarningsJson`/evidence.
      - Idempotent on `(ProviderName, ExternalStatementId)` and `(StatementId, MetricCode)`.
- [ ] Register `CodalDbFinancialStatementNormalizer` as `IFinancialPayloadNormalizer`.
- [ ] Verify `NetProfitMetricInputSource` (and any other input source) selects rows
      provider-agnostically by `MetricCode`; if it filters by provider, generalize it so CodalDb
      `NET_PROFIT` rows feed the engine.

## Tests

- [ ] `CodalDbStatementSelectionPolicyTests` (unit, ~6 tests): audited beats unaudited; latest
      representment chosen; consolidated/parent honored per config; soft-deleted excluded.
- [ ] `CodalDbFiscalPeriodMapperTests` (unit, ~4 tests): 3/6/9/12-month spans computed; Jalali
      retained; period length correct.
- [ ] `CodalDbFinancialStatementNormalizerTests` (unit, ~8 tests, EF in-memory): one statement
      produces income + balance rows; only mapped items written; `Net income`→`NET_PROFIT`;
      EPS mapped from item 160; idempotent re-run; selection flags recorded;
      `DerivedMetricRecalculationRequested` published.
