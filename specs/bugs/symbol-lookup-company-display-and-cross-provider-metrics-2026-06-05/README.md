# Bug: Symbol Lookup Displays Provider Identifiers and Misses Company-Linked Metrics

## Summary

Point lookup responses can show an ISIN or other provider linkage key in the `Symbol` column and
an empty company column. A lookup for Chadormalu P/E returned `IRO1CHML0001` instead of `کچاد`, left
the company as `—`, and showed `PE_TTM` as missing.

## Expected Behavior

- The response symbol must use `public."Companies"."TseSymbol"` for company-backed rows.
- The response company must use `public."Companies"."Name"`.
- Provider identifiers from `Symbols.SymbolCode`, ISINs, instrument codes, and external ids may be
  used for linkage, but they must not override company display fields.
- Point lookup metric resolution must search all symbol rows linked to the same `Companies.Id`.
  This supports mixed-vendor data where one provider resolves the company and another provider
  stores the derived metric.

## Observed Database State

Checked on 2026-06-05 against local PostgreSQL:

- Chadormalu company row exists:
  - `Name = معدنی و صنعتی چادرملو`
  - `TseSymbol = کچاد`
  - `CompanySymbol = CHML`
  - `SymbolIsin = IRO1CHML0001`
- The only linked symbol row currently found is `Symbols.SymbolCode = IRO1CHML0001`.
- No linked `DerivedMetrics` row currently exists for `MetricCode = PE_TTM`.

So the display issue is a code bug. The missing `PE_TTM` value is also real in the current local
database for Chadormalu, unless a later data sync/backfill inserts it.

## Fix Tasks

- Update Order 45 acceptance criteria and tasks to require company-backed display fields.
- Change symbol lookup execution to build rows from company display metadata.
- Change symbol lookup metric reads to aggregate latest metric rows across all symbols linked to
  the resolved company.
- Apply the same company display rule to scanner result rows.
- Add regression tests for `TseSymbol`, `Name`, and cross-provider company-linked metric lookup.
