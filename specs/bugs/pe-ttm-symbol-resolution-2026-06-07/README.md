# Bug: PE_TTM Symbol Resolution Inconsistency

## Summary

Point PE questions use the symbol-lookup path, not scanner threshold execution. The backend resolves
the user metric term `pe` to `PE_TTM`, resolves the user symbol through `Symbols.SymbolCode` and
`Companies` identifiers, then reads persisted rows from `DerivedMetrics`.

## Data Path

1. `LlmAiIntentDetector` routes PE point lookups to `DetectedIntent.SymbolLookup` via the
   deterministic PE/P/E point-lookup rule when no threshold/comparison is present.
2. `LlmSymbolLookupParser` extracts `(symbolName, metricTerm)` and resolves `pe` through the metric
   alias resolver to `PE_TTM`.
3. `EfCoreSymbolMetricLookupService` resolves the symbol with `EfCoreSymbolNameResolver`.
4. The lookup service queries `Symbols` for the resolved code, expands to all symbols with the same
   `CompanyId`, and reads `DerivedMetrics` for `PE_TTM`.
5. `PE_TTM` is a stored derived metric. It is not calculated at answer time. Current policy uses
   CyclicalWaves `PE_RATIO` line items as a passthrough source into `DerivedMetrics.PE_TTM`.

## Local Evidence

Checked local PostgreSQL on 2026-06-07:

- `PE_RATIO` source line items exist: 532.
- `PE_TTM` derived rows exist: 432 non-null rows.
- `شپنا` has a CyclicalWaves `DerivedMetrics.PE_TTM = 5.17`.
- `شبندر` has a CyclicalWaves `DerivedMetrics.PE_TTM = 5.06`.
- `پارسان` resolves to a NADPCO company/symbol row, but that company-linked symbol has no
  `DerivedMetrics.PE_TTM`.
- `کگل` resolves to a NADPCO company/symbol row, but that company-linked symbol has no
  `DerivedMetrics.PE_TTM`.
- No `PE_RATIO` source line item was found locally for NADPCO external company ids `4` (`کگل`) or
  `685` (`پارسان`).

## Root Cause

The reported inconsistency is data/provider coverage, with a secondary linkage inconsistency from
mixed provider rows:

- `شپنا` and `شبندر` resolve because CyclicalWaves supplied PE ratio snapshots and they were
  promoted to `DerivedMetrics.PE_TTM`.
- `پارسان` and `کگل` resolve as symbols/companies, but the current local dataset lacks PE ratio
  source rows and derived `PE_TTM` rows for them.
- The answer prompt is not the cause. The lookup is deterministic and only reads persisted table
  cells.

## Fix Applied

- Added PE-specific structured diagnostics in `EfCoreSymbolMetricLookupService`:
  - user query
  - normalized metric
  - detected and normalized symbol
  - resolved symbol id and company id
  - candidate symbol ids
  - SQL/table source description
  - raw `PE_TTM` value
  - missing reason
  - confidence decision reason
- Added symbol-lookup missing-data warnings for missing `PE_TTM` cells so confidence scoring and
  response payloads expose the missing-data reason.

## Verification

```powershell
dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore --filter "FullyQualifiedName~SymbolLookupEndpointTests|FullyQualifiedName~PeSymbolLookupRegressionTests|FullyQualifiedName~SymbolLookupParserTests|FullyQualifiedName~AiIntentDetectorTests"
```

Result: passed, 37 tests total.
