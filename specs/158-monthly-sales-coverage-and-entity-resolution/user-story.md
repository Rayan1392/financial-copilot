# User Story: Monthly Sales Coverage and Canonical Company Resolution

## Feature Folder

`158-monthly-sales-coverage-and-entity-resolution`

## Layers / Area

Layers: backend
Area: AI, FinancialData, Conversations

## Priority

P2

## Complexity

M

## Depends On

None

Reads, without needing changes to: 135 (ReportingType routing), 150 (shared routing), 155 (classifier v1, unchanged), 156 (snapshot card).

## References

- `specs/158-monthly-sales-coverage-and-entity-resolution/design-proposal.md`. Gate A approved by the owner on 2026-11-10 for D3 (canonical priority only), D4 and D5.
- `specs/158-monthly-sales-coverage-and-entity-resolution/read-only-checks.sql`
- `docs/domain-glossary.md`; `docs/company-master-data-model.md`; `docs/noavaran-current-api-ingestion.md`
- `specs/POLICY-V1-FREEZE.md`; `specs/135-noavaran-reporting-type-routing/Design.md` (§Verified ReportingType-to-endpoint mapping); `specs/141-schema-aware-semantic-query-engine/curated-catalog-review.md` (§ReportingType, owner-supplied Persian labels)
- Data model: `FinancialIngestionDbContext`, `NormalizedCompanyRow` (`Companies`), `CompanyMonthlyActivityTrendSnapshotRow`, `NoavaranCompanyScope`
- `specs/test-baseline.md`
- Owner decisions: OQ-026 (explanatory reply now; ask the vendor; no new ingestion), OQ-028 (no new Feature 155 bucket; a separate reason code). OQ-030 (classifier versioning) remains open.

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a customer asking for the monthly sales of a company,
I want the system to name the company I asked about and tell me the true reason when no monthly data can exist for it,
so that I am not told a large symbol is ambiguous, and I am not told that my request is supported when the data source does not cover that company.

## Business Context

Demand theme T07 (`specs/155-admin-unanswered-question-demand-report/`): 23 customers and 36 questions about monthly sales or production that received no useful answer. The owner's two goals for this feature:

1. A customer asking for the monthly sales of a company that has no data gets the true reason.
2. Large symbols such as فملی and فولاد are not reported as ambiguous.

Web chat and Telegram use the same reply text.

## Scope

- **D3, canonical priority only.** On the V2 path, the exact-match steps of `CanonicalQueryEntityResolver` prefer the eligible Noavaran row, then another Noavaran row, then another provider's row. A hit in a lower tier no longer outranks or blocks a higher tier.
- **D4.** A monthly-sales turn with no data and a known no-route company type gets the owner's explanatory reply. The Outcome stays `NoData`; the reason becomes `monthly_report_not_covered`.
- **D5.** On the V2 path, `MonthlyActivityTrendQueryUseCase` reads the Noavaran row for the symbol, not another provider's row with the same symbol.
- **Two-company clarification (OQ-061, option (b)).** On V2, when a capability has no multi-company slot and two distinct companies resolve exactly, the turn returns `ClarificationNeeded` with a new reason code and a pending disambiguation holding both companies. It produces no single-company frame and no `entity_ambiguous`.
- **A read-only verification task** that produces the owner's per-symbol list and reason once a read-only connection exists.

## Out Of Scope

- D1 (BUG-008), D2 (BUG-009), D6 (BUG-012). Not approved.
- The separator fallback-path part of D3. Not approved. It is not needed for the clarification, because the clarification reads model-proposed entity spans; see Assumptions.
- The `entity_source` tag.
- Loading `Companies` once per turn. Canonical priority partitions rows the resolver already loads, so it does not need this.
- New ingestion, new vendor endpoints, schema changes, migrations.
- `specs/155-admin-unanswered-question-demand-report/queries.sql`, its buckets, and its gap rate (OQ-030).
- OQ-011 and OQ-023.
- Other consumers of `ResolveBySymbolAsync`, and the snapshot card's section text.
- Any behaviour change on V1.

## Functional Requirements

### Canonical priority (D3)

1. On V2, the exact step of `ResolveMentionAsync` evaluates three ordered tiers: (T1) rows eligible under `NoavaranCompanyScope`; (T2) other `NoavaranCurrentApi` rows; (T3) other providers.
2. Inside a tier, the existing order applies: exact ticker, then exact company name, then approved alias. The first tier with a hit decides. One row is `Resolved`. More than one row is `Ambiguous`.
3. Substring (`normalized_identity_variant`) and fuzzy steps are unchanged and run only when no tier has an exact hit.
4. `ResolveExactTickerFromTextAsync` is unchanged.
5. Eligibility has one owner. `NoavaranCompanyScope` gains an in-memory predicate that uses the same constants as its query filter.
6. On V1 (`Feature154RoutingScope.IsActive` is false), `ResolveMentionAsync` runs today's algorithm unchanged.

### Monthly coverage reply (D4)

7. New Application port `IMonthlyReportCoverageReader.ReadAsync(symbol, cancellationToken)` returns a plain `MonthlyReportCoverage`: `Kind` is one of `HasRoute`, `NoRoute`, `OutsideScope`, `Unknown`, `CompanyAmbiguous`, and `CategoryFa` / `CategoryEn` are set only for `NoRoute`.
8. `MonthlyActivityTrendCapabilityExecutor` calls the port only when the trend result is null and the V2 scope is active.
9. The executor maps the coverage kind as in the table under "AI Query Path".
10. The port never throws, except `OperationCanceledException` when the caller cancels. A failure or timeout is logged at Warning and returns `Unknown`.
11. The code-to-category map lives beside `NoavaranMonthlyReportTypeResolver`. Application holds no provider code.

### Monthly company resolution (D5)

12. On V2, the use case resolves through a new Infrastructure resolver: exact symbol match inside the eligible rows first; then, when no eligible row matches, `ICompanyResolutionOutcomeService.ResolveAsync(symbol, "NoavaranCurrentApi")`.
13. On V1, the use case calls `ResolveBySymbolAsync` as today.
14. `Ambiguous`, `NotFound`, `InvalidIdentifier` and `UnsupportedProviderCompany` make the use case return null, as for any company it cannot read.

### Two-company clarification (OQ-061)

15. On V2, when the capability has a `symbol` slot and no `symbols` slot, and at least two distinct companies resolve by an exact kind (`exact_ticker`, `exact_company_name`, `approved_alias`) from **model-proposed** company mentions, the gate replaces the company slot with an `Invalid` slot whose detail is `multiple_companies_not_supported`, and records a pending `Disambiguation` holding those companies as candidates.
16. The dispatcher returns `ClarificationRequired` with reason `multiple_companies_not_supported`. The turn produces no frame execution for a single company and no `entity_ambiguous`.
17. A Persian month name proposed as a mention does not count as one of the companies. Deterministic mentions are not counted. Capabilities with a `symbols` slot (for example `symbol_metric_lookup`) are unchanged.

## Data / Schema

No schema change and no migration. Reads `Companies.ReportingType`, `PrecedencyRight`, `MarketId`, `ProviderName`, symbol columns; and `CompanyMonthlyActivityTrendSnapshots` as today.

## API (if any)

No contract change. `monthly_report_not_covered` is an additive string in the persisted assistant payload.

## AI Query Path (if any)

V2 capability `monthly_activity_trend`, executor `MonthlyActivityTrendCapabilityExecutor`.

When the use case returns null:

| Coverage | Status | Reason code | Customer sees |
|---|---|---|---|
| `NoRoute` | `NoData` | `monthly_report_not_covered` (new) | The owner's reply below |
| `OutsideScope` | `NoData` | `data_stale_or_ineligible` | The owner's outside-scope sentence below (OQ-062) |
| `HasRoute` | `NoData` | `supported_but_no_rows` | Default no-data sentence, unchanged |
| `Unknown` (including port failure) | `NoData` | `supported_but_no_rows` | Default no-data sentence, unchanged |
| `CompanyAmbiguous` | `DisambiguationRequired` | `entity_ambiguous` | Existing sentence `نام نماد یا شرکت به‌طور قطعی مشخص نشد. لطفاً نام نماد دقیق را وارد کنید.` |

Billing is unchanged: `NoData` is zero-charge, and `DisambiguationRequired` uses the existing clarification billing.

### Reply text for `NoRoute`

Template:

```text
پوشش گزارش ماهانهٔ فروش برای شرکت‌های {categoryFa} در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «{symbol}» نمودار فروش ماهانه نداریم.
```

`categoryFa` uses the owner-supplied labels of `specs/141-.../curated-catalog-review.md`. With the invented symbol `نمونه`, the rendered reply is:

| Code | `categoryFa` | Rendered reply |
|---|---|---|
| 1000001 | ساختمانی | پوشش گزارش ماهانهٔ فروش برای شرکت‌های ساختمانی در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «نمونه» نمودار فروش ماهانه نداریم. |
| 1000002 | سرمایه‌گذاری | پوشش گزارش ماهانهٔ فروش برای شرکت‌های سرمایه‌گذاری در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «نمونه» نمودار فروش ماهانه نداریم. |
| 1000003 | بانکی | پوشش گزارش ماهانهٔ فروش برای شرکت‌های بانکی در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «نمونه» نمودار فروش ماهانه نداریم. |
| 1000004 | لیزینگی | پوشش گزارش ماهانهٔ فروش برای شرکت‌های لیزینگی در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «نمونه» نمودار فروش ماهانه نداریم. |
| 1000006 | بیمه‌ای | پوشش گزارش ماهانهٔ فروش برای شرکت‌های بیمه‌ای در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «نمونه» نمودار فروش ماهانه نداریم. |
| 1000007 | حمل و نقل دریایی | پوشش گزارش ماهانهٔ فروش برای شرکت‌های حمل و نقل دریایی در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «نمونه» نمودار فروش ماهانه نداریم. |
| 1000009 | تأمین سرمایه | پوشش گزارش ماهانهٔ فروش برای شرکت‌های تأمین سرمایه در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «نمونه» نمودار فروش ماهانه نداریم. |

English reply, used only when the reply language is `en`. It is needed because the outcome policy has an English branch, and the owner has not yet seen this sentence. It omits the symbol on purpose: the language guard (`AiDialogueOutcomePolicy.IsCompatibleWithLanguage`) rejects a detail that contains Persian script, and `DisplaySymbol` is usually Persian.

```text
Monthly sales report coverage for {categoryEn} companies is not available from the current data source, so there is no monthly sales chart for this symbol.
```

`categoryEn`: construction, investment, banking, leasing, insurance, maritime transportation, investment banking.

### Reply text for `OutsideScope` (OQ-062)

Persian, the owner's exact sentence:

```text
این نماد در فهرست شرکت‌های تحت پوشش گزارش ماهانه نیست.
```

English, proposed, contains no Persian script:

```text
This symbol is not on the list of companies covered by monthly reports.
```

### Two-company clarification text (OQ-061)

Proposed; the owner has not seen it. The reason is `multiple_companies_not_supported`, outcome `ClarificationNeeded`. The text names no ticker, because the clarification path carries no detail.

```text
این پرسش فقط برای یک نماد قابل پاسخ است. لطفاً نماد موردنظرتان را بنویسید.
```

```text
This question can be answered for one symbol only. Please send the symbol you want.
```

## UI (if any)

No screen change. Web chat and Telegram render the assistant text as today.

## Non-Functional Requirements

- The port call happens only on the no-data branch. It reads one company by key after resolution; timeout 2,000 ms; no retry.
- A port failure is visible in logs (Warning, structured, no secrets) and does not fail the turn.
- No new outbound call, no new configuration key.

## Acceptance Criteria

Canonical priority

- [x] AC-1: With an eligible row and two other-provider rows that carry the symbol in `Ticker`, `روند فروش ماهانه کگل` produces a frame whose company is the eligible row, and no `entity_ambiguous` result.
- [x] AC-2: With an eligible row and a non-eligible `NoavaranCurrentApi` row with the same symbol, the company is the eligible row.
- [x] AC-3: With an eligible row and one other-provider row carrying the symbol in `Ticker`, `CanonicalId` is the eligible row.
- [x] AC-4: Two eligible rows with the same symbol stay `Ambiguous`. Two rows of a provider other than Noavaran, with no Noavaran row, stay `Ambiguous`.
- [x] AC-5: Substring and fuzzy results are unchanged when no exact hit exists.
- [x] AC-6: Under `Feature154RoutingScope.EnterFrozenV1`, the row shape of AC-1 still returns today's `Ambiguous`.
- [x] AC-7: `NoavaranCompanyScope`'s in-memory predicate agrees with its query filter on a table of rows; the market IDs exist in one place.

Monthly coverage reply

- [x] AC-8: For each of the seven no-route codes, a no-data turn returns `NoData` with reason `monthly_report_not_covered` and exactly the rendered text in the table above.
- [x] AC-9: With reply language `en`, the reply is the English sentence with the matching `categoryEn`.
- [x] AC-10: `1000000`, `1000005` and `1000008` give `supported_but_no_rows` and the default sentence.
- [x] AC-11: Null `ReportingType`, an unknown code, and a symbol not in the catalog give `supported_but_no_rows` and the default sentence.
- [x] AC-12: A non-eligible Noavaran row, and a company found only with another provider, give `data_stale_or_ineligible` with exactly `این نماد در فهرست شرکت‌های تحت پوشش گزارش ماهانه نیست.` (or the English sentence when the reply language is `en`), whatever the `ReportingType`.
- [x] AC-13: A port failure or timeout leaves the turn `NoData` with `supported_but_no_rows`, and writes one Warning log with the outcome `coverage_read_failed`.
- [x] AC-14: When the trend result is not null, the port is not called. Under frozen V1, the port is not called.
- [x] AC-15: Reply text and category labels have one owner each; no `ReportingType` number exists in Application.

Monthly company resolution

- [x] AC-16: With a Noavaran eligible row and another provider's row with the same symbol, and a snapshot under the Noavaran id, the use case returns that snapshot on V2. This holds when the other row carries the symbol in `Ticker` and when it carries it in `TseSymbol`.
- [x] AC-17: With two Noavaran rows of one symbol of which one is eligible, the use case returns the eligible row's snapshot.
- [x] AC-18: Two eligible rows with one symbol make the use case return null, and the executor returns `DisambiguationRequired` with `entity_ambiguous`.
- [x] AC-19: Under frozen V1, the same rows behave exactly as today.
- [x] AC-20: The V2 executor, the snapshot card, and the V2 workflow's legacy route all read through the new resolution. V1's route does not. The snapshot card's text for a missing month is unchanged. Verified at the use-case level, where the scope decides; the three V2 callers were not each exercised end to end.

Two-company clarification

- [x] AC-23: A model proposal of `monthly_activity_trend` with two exact tickers (`روند فروش ماهانه کگل/شپنا`, invented) produces no single-company slot, no `entity_ambiguous`, and a pending `Disambiguation` holding both companies.
- [x] AC-24: The dispatcher returns `ClarificationRequired` with reason `multiple_companies_not_supported`; the reply is the proposed text for `fa` and `en`.
- [x] AC-25: Replying with one of the tickers runs the capability for that ticker.
- [x] AC-26: `symbol_metric_lookup` keeps multi-company handling. A single exact company, a variant match, a month-name mention, and the deterministic path never produce the clarification. Frozen V1 is unchanged.

Verification

- [x] AC-21: Green means no new failure against `specs/test-baseline.md`. Every test in `tasks.md` §Regression tests failed before its fix, for the stated reason.
- [x] AC-22: No acceptance claim is made about the Feature 155 gap rate. D4 turns stay in the `no_data` bucket.

## Assumptions

- The Persian `categoryFa` labels are the owner-supplied labels in the Feature 141 review. The owner has seen the template, not each rendered line.
- A company with a no-route code may still publish monthly reports elsewhere. The reply says the source does not cover it, not that no report exists.
- Per-symbol causes are Unverified until the read-only check runs.
- The clarification reads model-proposed spans. On the deterministic fallback path, used only when the model proposal is unavailable, two tickers joined by a separator keep today's behaviour. The separator fallback-path part of D3 is out of scope.
