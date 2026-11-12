# 0001 — Per-capability chat data source

Status: Accepted by the owner on 2026-11-10
Related: specs/159-capability-source-map-comprehensive-analysis/, specs/148-cyclicalwaves-v2-independent-financial-data-provider/ (C2), specs/160-valuation-pe-and-gauge-equilibrium/ (parked)

## Context

Chat chooses one financial data provider for the whole application at start-up (`FinancialData:ChatProvider`,
`NoavaranAmin` or `CyclicalWaves`). Production runs `NoavaranAmin`. Some capabilities exist only in CyclicalWaves v2
(comprehensive analysis, P/E and P/S gauges, equilibrium range, P/E and P/S values), and their data is already
ingested into schema `cyclicalwaves`. Feature 148 decided that NoavaranAmin-mode chat never reads those tables (C2
and extension decisions 5 and 7). Customers ask for these capabilities (Feature 155 themes T01 and T03).

## Decision

Chat keeps one base provider. A configuration map `FinancialData:CapabilitySources` may assign a different
provider to individual capabilities, keyed by capability route code. Only capabilities in a code-owned allowlist
may be overridden; anything else fails start-up validation. The capability is detected first; the source is
looked up after detection. `IChatCapabilityAvailability` is the single owner of that lookup. There is no fallback
between providers: an overridden capability answers only from its assigned source, and missing data is reported
as missing. Values from one provider are never written into the other provider's stores or combined into one
calculated value.

Step 1 (Feature 159) allowlists `comprehensive_analysis`. Steps 2 and 3 extend the allowlist only through their
own approved features.

## Consequences

- Decision C2 of Feature 148 now applies only while `comprehensive_analysis` is not mapped. Extension decisions 5
  and 7 stay in force for every capability not in the allowlist.
- One answer may contain values from two providers (for example analysis from CyclicalWaves and context metrics
  from NoavaranAmin); each part must name its source.
- Removing a map entry restores the previous behaviour without a code change.
- Tests must prove, per mapped capability, that only its tables are read from the other provider's schema.
- A full switch of `ChatProvider` remains possible and unchanged.
