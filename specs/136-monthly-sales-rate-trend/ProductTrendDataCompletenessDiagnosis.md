# Feature 136 Product Trend Data Completeness Diagnosis

Date: 2026-10-04

Scope: read-only investigation of `روند فروش آهن اسفنجی کچاد`. No production code, database, provider, or ingestion state was changed. This diagnosis distinguishes evidence in the existing acceptance report and query implementation from facts that require a live provider payload/database result.

## Summary

The trend can appear incomplete because the documented data acceptance re-ingestion ended at fiscal month `1405/04`, while the query itself anchors its rolling 12-position window to the newest accepted month that has a sales observation for the resolved product. The query preserves missing fiscal positions as gaps; it does not substitute company-level periods or a company trend. Thus later months can be absent either because the provider had no qualifying report/product observation or because those months were not ingested/accepted. The current retained evidence does not establish which of these applies to `1405/05`–`1405/07`.

This is not evidence of a trend-window truncation defect. The window behavior matches the approved Feature 136 semantics. There is insufficient retained source evidence to attribute the later-month holes to provider omission versus ingestion omission/rejection.

## Query and endpoint source

The request is served by the real `MonthlyProductTrendQueryUseCase` through the V2 product-trend path. It obtains available periods for the company, loads the accepted normalized observations, resolves the requested product by its cross-period `ProductKey`, and then creates the series for that key.

`EfCoreMonthlyProductComparisonRepository` restricts both available periods and period rows to accepted `ProductSales` reports with `OutputType=0`. Other output types, service-sales reports, and rejected report revisions are not query inputs. For KCHAD the prior real acceptance report recorded the resolved key as `NoavaranCurrentApi:3:TITLE:آهن اسفنجی|UNIT:تن` and a 12-position product result.

The default window is the latest period with a `SalesAmount` for the selected product plus the preceding eleven fiscal positions. A period without a row for that ProductKey remains a gap/unavailable point. In particular, latest company-report month is not used as a proxy for latest product observation. This follows the approved semantics in `Design.md` and `UserStory.md`.

Relevant implementation: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyProductTrendQueryUseCase.cs` and `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/EfCoreMonthlyProductComparisonRepository.cs`.

## Existing ingestion and period evidence

`RuntimeFixReport.md` records a successful targeted real re-ingestion for KCHAD (`ExternalCompanyId=3`), `ProductSales/OutputType=0`, fiscal range `1404/01`–`1405/04`: 16 reports requested and completed, 234 current accepted normalized rows, 15 rejected candidates, zero processing failures. It does not record requests or outcomes for `1405/05`, `1405/06`, or `1405/07`.

The report’s real KCHAD trend had 12 points, with a recorded valid calculated-rate sample at `1404/11`. It does not enumerate the full 12-point payload or provide a month-by-month row/revision manifest. Therefore the previously documented report cannot prove exactly which of `1405/03`–`1405/07` has a selected-key row, whether any month has a newer rejected/superseded revision, or whether an accepted report exists with the product observation omitted.

| Fiscal period | Raw NADPCO ProductSales/0 evidence retained in acceptance report | Normalized accepted row evidence retained | Diagnosis |
|---|---|---|---|
| 1405/03–1405/04 | Not enumerated by month | Included in the 16-report range, but no per-month product-key rows/revisions listed | Cannot establish ProductKey continuity from report totals alone |
| 1405/05 | No raw request/payload result recorded | Outside the documented re-ingestion range | Provider-missing vs. not-ingested is undetermined |
| 1405/06 | No raw request/payload result recorded | Outside the documented re-ingestion range | Provider-missing vs. not-ingested is undetermined |
| 1405/07 | No raw request/payload result recorded | Outside the documented re-ingestion range | Provider-missing vs. not-ingested is undetermined |

The accepted-row and rejected-candidate totals are for all normalized rows/candidates in the company/range, not a count for this product or a per-period acceptance/revision history. They must not be interpreted as product-level completeness.

## Raw payload and revisions

The retained report contains aggregate processing counts, and report records expose payload checksum metadata. A checksum identifies payload content but is not the payload itself. No raw NADPCO response body or per-request result for `1405/05`–`1405/07` is included in the available Feature 136 acceptance evidence. Accordingly, the investigation cannot confirm whether NADPCO supplied `آهن اسفنجی` in those months.

Similarly, the available report does not list all KCHAD monthly report revisions and their accepted/superseded/rejected statuses for `1405/03`–`1405/07`. Revision selection is performed among qualifying accepted normalized reports; there is no evidence here that the trend silently chose an older revision, nor enough evidence to rule that out for an individual month.

Strict query input is `ProductSales` + `OutputType=0` + accepted. A matching product row under another output type would not fill a gap by design.

## Resolver continuity and endpoint payload

The existing real acceptance evidence establishes exactly one resolved KCHAD key for `آهن اسفنجی`, and one returned 12-point result. It does not preserve the complete point JSON or a month-by-month ProductKey continuity table from `1405/03` through `1405/07`. The API process/database being locally available is not by itself a substitute for an executed, reproducible query against the current database; this investigation did not mutate or re-ingest data to produce missing evidence.

Consequently:

- Identity is confirmed by the prior acceptance record; the current report establishes no new resolver regression.
- The 12-position fiscal-window behavior is confirmed by source and prior result shape.
- Exact current endpoint point values and precise missing periods in `1405/05`–`1405/07` are not established by retained evidence in this diagnosis.
- Periods must be interpreted as fiscal Shamsi months, not Gregorian calendar months or report-publication dates.

## Classification and recommended next action

Classification: **data-evidence gap; provider omission versus ingestion omission is undetermined**. No Feature 136 code defect is demonstrated. No production code change is indicated by the current evidence.

To conclusively classify each month, obtain the provider request/result manifest or raw response for KCHAD ProductSales output type 0 for `1405/05`–`1405/07`, then compare each provider report/revision and product row against accepted normalized rows selected by the same strict filters. The existing targeted DataAdmin command documented in `RuntimeFixReport.md` is the supported mechanism if an operational re-ingestion is authorized and credentials are available. It uses `POST http://localhost:5074/api/v1/admin/noavaran-current/single-company-monthly-ingestion` with `externalCompanyId=3`, `outputType=0`, and explicit Shamsi start/end months. The prior recorded range ended at `1405/04`; do not infer later-month completeness from that run. This read-only diagnosis did not run the ingestion job.

If a provider response contains the expected product row but the accepted normalized revision lacks it, investigate ingestion normalization/revision persistence. If the provider response itself has no qualifying report or product row, classify the period as provider-missing for this query. If such a row exists only in a nonzero output type, its exclusion is expected under Feature 136 semantics. Only if accepted type-0 normalized rows have the same ProductKey and valid sales observation but the endpoint omits them should query logic be investigated; likely files would be the repository and `MonthlyProductTrendQueryUseCase.cs` named above.

## Conclusion

The visible incompleteness is consistent with the documented product-specific window and the documented re-ingestion coverage ending at `1405/04`. It cannot yet be attributed conclusively to NADPCO or ingestion, because the available evidence lacks raw later-period provider responses and a per-period accepted/rejected revision plus ProductKey row manifest. No unrelated code was changed and no ingestion was executed.

FEATURE_136_PRODUCT_DATA_COMPLETENESS_DIAGNOSED
