# Tasks

## Application — Ratio Provider Capability

- [ ] Add `ProviderDataset.FinancialRatios` to the `ProviderDataset` enum.
- [ ] Add `IFinancialRatioProvider` with
      `Task<ProviderRawPayload> FetchFinancialRatiosAsync(string externalCompanyId, CancellationToken)`.
- [ ] Extend `FinancialDataSyncProcessor`'s fetch switch to handle
      `ProviderDataset.FinancialRatios` via the resolved `IFinancialRatioProvider`.
- [ ] Add `FinancialRatios` to the admin data-sync trigger surface in `012` (a new
      `POST /api/v1/admin/data-sync/financial-ratios` endpoint publishing the dataset request).

## Infrastructure — Provider Query

- [ ] Implement `IFinancialRatioProvider` on `CodalDbDataProviderClient`:
      query `FinancialRatios` JOIN `FinancialRatioItems` filtered by `CompanyId` and the mapped
      `ItemID` set; project canonical rows; serialize to a deterministic `ProviderRawPayload`
      (`ProviderDataset.FinancialRatios`, `Endpoint = "codaldb://financial-ratios/{CoID}"`).
      Page/stream the read; never full-table scan.

## Domain / Semantic — Ratio MetricCode definitions (015 catalog)

- [ ] Add `CodalDbRatioItemMap` — governed `FinancialRatioItems.Id → MetricCode` dictionary per
      the curated table (verified ids: 65/41066, 8191, 4069, 6901, 4071, 41006, 4100, 41067,
      20706, 4106, 4136, 4138, 4139, 4140, 4135, 4117). Resolve the `Current ratio` duplicate by
      row coverage.
- [ ] Add `FinancialMetricDefinition` + bilingual `MetricAlias`es to
      `PhaseOneFinancialSemanticCatalog` for all 16 ratio metric codes, with correct
      `MetricUnit` (Ratio / Percentage / Days / Amount). Persian aliases from
      `FinancialRatioItems.Title`.
- [ ] Sample CodalDB to determine percentage encoding (fraction vs percent) and document the
      normalization to the platform `Percentage` convention.

## Infrastructure — Normalizer (vendor-precomputed → DerivedMetricRow)

- [ ] Add `…/Ingestion/CodalDb/CodalDbRatioNormalizer.cs`
      (`ProviderName = "CodalDb"`, `Dataset = FinancialRatios`):
      - Apply `CodalDbStatementSelectionPolicy` per `(CompanyId, PeriodEnd, PeriodType, ItemID)`.
      - Resolve the company's `SymbolId` (from normalized symbol rows).
      - Write a `DerivedMetricRow` per value: `CalculationPolicyVersion = "codal-ratio-source-v1"`,
        `SourceEvidenceJson` marking vendor-precomputed (CodalDb + ratio item id), mapped unit,
        period from the ratio row.
      - Idempotent on the `DerivedMetricRow` unique key.
      - Does **not** invoke `IFinancialMetricCalculator`.
- [ ] Register `CodalDbRatioNormalizer` as `IFinancialPayloadNormalizer`.

## Tests

- [ ] `CodalDbRatioNormalizerTests` (unit, ~8 tests, EF in-memory): each mapped ratio persisted
      as a `DerivedMetricRow` with `codal-ratio-source-v1` policy + vendor source evidence;
      canonical variant selected; percentage normalization correct; idempotent re-run; distinct
      policy version does not collide with engine-calculated rows.
- [ ] Scanner integration test: filtering by `RETURN_ON_EQUITY` / `CURRENT_RATIO` returns Codal
      companies and the explanation cites the vendor-precomputed policy version.
