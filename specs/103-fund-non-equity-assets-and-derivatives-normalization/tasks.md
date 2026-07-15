# Tasks — Fund Non-Equity Assets and Derivatives Normalization

## 1. Domain and Contracts

- [ ] Add contracts/entities for asset allocation, commodity certificates, bank deposits, derivatives, derivative type, option side/type, asset class, and resolution state.
- [ ] Make Feature 103 owner of normalized non-equity position rows and summary/detail reconciliation.
- [ ] Keep source-disclosed valuation separate from any later calculated market valuation.

## 2. Asset Allocation Summary

- [ ] Map `سرمایه گذاری ها` rows into governed asset classes such as EquityAndRights, CommodityCertificates, BankDeposits, Derivatives, CashAndOther, and Unknown.
- [ ] Store raw labels and normalized asset-class codes.
- [ ] Parse cost, market/net-sale value, and total-assets percentage for each available period context.
- [ ] Persist section totals and detect totals that cannot be mapped due to source formula errors.
- [ ] Do not fabricate missing classes or force the sum to 100%.

## 3. Commodity Certificate Parser

- [ ] Define versioned header mapping for `سرمایه‌گذاری درگواهی سپرده`.
- [ ] Parse beginning, purchase, sale, and ending quantities/values plus ending price and asset weight.
- [ ] Extract instrument symbols/codes embedded in descriptions when present.
- [ ] Add governed mappings for gold bullion, copper cathode, rebar, and future commodities without hardcoding only the sample rows.
- [ ] Reconcile ending quantity and section totals.
- [ ] Preserve source names and unresolved commodity/instrument rows.

## 4. Bank Deposit Parser

- [ ] Define versioned header mapping for `(2)سپرده`.
- [ ] Parse bank name, beginning balance, increases, decreases, ending balance, and asset weight.
- [ ] Add a governed bank alias/catalog mapping; do not map by uncontrolled fuzzy match alone.
- [ ] Check `Ending = Beginning + Increase - Decrease` within tolerance and persist differences.
- [ ] Reconcile bank detail total with allocation summary when available.
- [ ] Treat deposits as fund assets, not the user's cash account.

## 5. Derivative Parser

- [ ] Parse the protective-put section and the ordinary option-position section independently from `اوراق مشتقه`.
- [ ] Normalize contract name, underlying, strike, quantity, expiry/exercise date, effective return, position side/type, cost/value, and weight where present.
- [ ] Resolve derivative and underlying through canonical trading-instrument/company metadata when possible.
- [ ] Preserve unresolved instrument names and feed review items to Feature 101.
- [ ] Add contract-multiplier field/policy only when supplied by canonical instrument metadata; do not assume a universal multiplier.
- [ ] Detect duplicate/current-versus-comparative blocks through header context.

## 6. Hedge-Coverage Evidence

- [ ] Add a deterministic optional calculation comparing protective-put covered quantity with matching underlying ending holdings.
- [ ] Return `Covered`, `PartiallyCovered`, `OverCovered`, `NoMatchingHolding`, or `UnknownInputs`.
- [ ] Do not label ordinary call/put positions as hedges without matching evidence.
- [ ] Store calculation version, inputs, and source references.

## 7. Persistence and Repositories

- [ ] Add EF Core tables/configuration/migration for the four proposed models.
- [ ] Add natural unique keys by report, period context, source logical row, and normalized/raw identity.
- [ ] Add indexes for fund/period, asset class, commodity, bank, derivative type, underlying company, expiry, and unresolved status.
- [ ] Implement replace-by-source-revision/idempotent upsert.
- [ ] Add repositories and internal queries for allocation, deposits, commodities, derivatives, and unresolved rows.

## 8. Reconciliation and Quality

- [ ] Reconcile detailed market values and weights to allocation summaries with configurable absolute/percentage tolerance.
- [ ] Persist reconciliation status rather than modifying source values.
- [ ] Distinguish unavailable summary due to formula error from a genuine zero.
- [ ] Generate Feature 101 review items for unit ambiguity, unresolved identity, duplicate logical rows, impossible dates, and material total differences.

## 9. Observability and Tests

- [ ] Emit parsed counts by asset type, unresolved rate, summary/detail differences, deposit equation failures, derivative underlying resolution, and hedge-coverage availability.
- [ ] Unit-test all header mappings, commodity extraction, bank aliases, derivative-name parsing, Jalali dates, and numeric/error states.
- [ ] Integration-test persistence, reprocessing, mapping review, detail-summary reconciliation, and protective-put coverage against equity holdings.
- [ ] Given the sample workbook, when normalized, then gold/copper/rebar, bank deposits, protective puts, and option positions remain distinct source-traceable asset types.

## Completion Gate

- [ ] Keep tasks unchecked until all four non-equity sections are normalized, detail/summary reconciliation is explicit, derivative intent is not guessed, and source evidence is reproducible.
