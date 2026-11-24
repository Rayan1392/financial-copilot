# BUG-047: stale own-industry routing tests

Status: Gate A: Approved by the owner on 2026-11-24

## Cause

`IndustryComparisonRoutingEndpointTests.Api_ResolvesExactTickerAndReturnsExistingIndustryValuationTable` (six phrasings) asserted the Feature 148 retirement: the answer had to equal `ChatCapabilityAvailability.RetiredMessageFa`. Since the CyclicalWaves v2 relative valuation (`IndustryRelativeValuation:CyclicalWavesV2Enabled`, true in `appsettings.json`), `ChatCapabilityAvailability.EvaluateForV2` makes `symbol_vs_industry_relative_valuation` available with the NoavaranAmin provider, so the answer is the comparison table. Production behaves as intended; only the test was out of date.

## Fix (tests only, no production code)

- The six cases check the current behavior: routing to `symbol_vs_industry_relative_valuation`, the comparison title, the کگهر row and the «میانگین صنعت» row, and no retired message.
- A new `IndustryComparisonRetiredEndpointTests` keeps the Feature 148 guarantee: with the v2 relative valuation switched off (`IndustryComparisonRetiredApiFactory`), the same question still gets the retired message.
