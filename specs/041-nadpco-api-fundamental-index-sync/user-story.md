# NADPCO API Fundamental Index Synchronization

## User Story

As a scanner user, I want curated NADPCO fundamental indexes persisted as scannable metrics so
I can filter companies by vendor-provided ratios and indicators with explicit provenance.

## Source Endpoint

```http
POST /api/v2/CompanyFundamentalIndex/Values
```

Requests accept bounded `companyIds`, optional `companyIndexIds`, year range, period type, and
variant filters. Responses contain company-period headers and nested indexes with index ID,
title, group ID, group title, value, and unit.

## Acceptance Criteria

1. Add a dedicated provider dataset for NADPCO fundamental indexes and fetch only bounded
   company/index batches.
2. Establish a reviewed allowlist mapping vendor `companyIndexId` values to canonical
   `MetricCode`, unit, semantic aliases, and source policy version.
3. Persist curated vendor-provided observations as `DerivedMetricRow` values with
   `CalculationPolicyVersion = "nadpco-api-fundamental-index-source-v1"` and source evidence.
4. Distinguish vendor-precomputed observations from engine-calculated metrics; never route
   source indexes through `IFinancialMetricCalculator`.
5. Apply deterministic canonical variant selection and idempotent upserts.
6. Retain vendor index group and title metadata as evidence.
7. Require sampled scale verification before activating percentage-like metrics.
8. Make curated indexes scannable without scanner-engine code changes.

## Out Of Scope

- Importing every vendor index automatically.
- Treating vendor titles as stable identifiers.
- Overwriting deterministic calculations.

