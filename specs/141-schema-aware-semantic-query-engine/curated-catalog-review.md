# Feature 141 — Curated Financial Semantic Catalog Review

Generated from `SemanticCatalog/catalog-scope.yaml`, selected table YAML metadata, and the external-source YAML record. This is a review proposal; no semantic entry is executable.

## Approval and evidence boundary

- Physical keys, column types, and declared foreign keys: live PostgreSQL `pg_catalog` snapshot, reconciled to EF Core.
- Provider and calculation semantics: application source and existing feature policies cited per table.
- ReportingType labels and the million-IRR business definition: owner-supplied definitions; labels are not inferred from codes.
- Company and instrument historical lineage: product-owner-confirmed; current synchronization behavior: source-code verified; deployed settings are unverified.
- IsAudited, IsRepresented, and IsComposing true/false meanings: `business-owner-confirmed`; the three flags are independent.
- Curated scope: 18 physical tables plus 1 external provider endpoint; all physical entries remain `candidate-pending-review`; `queryable: false`.

## Confirmed company and instrument lineage

Companies: `TSETMC → Novaran Amin → Novaran API → Financial Copilot Companies`. TradingInstruments: `TSETMC → direct TSETMC ingestion / historical migration → Financial Copilot TradingInstruments`. These are distinct source paths and identities.
Repository code registers TsetmcDirectFeedSyncService and TsetmcPollingWorker schedules end-of-day instrument sync when enabled and operational. Worker defaults enable TSETMC polling and disable StockMarketDB polling; deployment overrides/current worker state were not verified. StockMarketDbSyncService fetches instrument rows for watermarking but explicitly does not write TradingInstruments.
`Companies.Id` is the canonical company UUID; `ProviderName + ExternalCompanyId` is a provider-scoped company key. `TradingInstruments.Id` is the internal instrument UUID. TSETMC `InsCode` maps to `InstrumentCode`; `ExternalInstrumentId` is a deterministic GUID derived from InsCode; the ISIN-like source `InstrumentID` maps to `InstrumentIsin`. The optional `NormalizedCompanyId` FK is distinct from all of these. In current TSETMC persistence both `SourceChangedAt` and `LastSynchronizedAt` are set to adapter synchronization time, not a vendor change time.

## ReportingType mapping and monthly-report ingestion

| Code | Persian definition | English definition | Noavaran route |
|---:|---|---|---|
| 1000000 | تولیدی | Manufacturing | ProductSales |
| 1000001 | ساختمانی | Construction | Unsupported |
| 1000002 | سرمایه‌گذاری | Investment | Unsupported |
| 1000003 | بانکی | Banking | Unsupported |
| 1000004 | لیزینگی | Leasing | Unsupported |
| 1000005 | خدماتی | Services | ServiceSales |
| 1000006 | بیمه‌ای | Insurance | Unsupported |
| 1000007 | حمل و نقل دریایی | Maritime Transportation | Unsupported |
| 1000008 | کشاورزی | Agriculture | ProductSales |
| 1000009 | تأمین سرمایه | Investment Banking | Unsupported |

Manufacturing (`1000000`) and agriculture (`1000008`) use `ProductSales` v2. Services (`1000005`) use `ServiceSales` v3. The code has no verified endpoint for the remaining seven supplied categories. The current API monthly-activity boundary starts at Shamsi 1404. Endpoint support does not prove persisted coverage for every company or month.

### Units and null policy

`SalesAmount` is million IRR per the supplied business definition and corroborating trend persistence contract. A line-level `SalesRate` is source/provider supplied and implies million IRR per source quantity unit. The separate `MonthlyAverageSalesRate` snapshot is total monthly sales amount divided by total sales quantity for selected product rows when units are not mixed and total quantity is nonzero. Keep source `Unit`, preserve negative values, and do not substitute that snapshot calculation for line-level values.

## Financial statement flag definitions

| Physical field | Persian | English | `true` means | `false` means | Provenance/status |
|---|---|---|---|---|---|
| `IsAudited` | حسابرسی‌شده | Audited Financial Statement | Audited | Unaudited | business-owner-confirmed / confirmed |
| `IsRepresented` | تجدید ارائه‌شده | Restated Financial Statement | Restated | Not restated | business-owner-confirmed / confirmed |
| `IsComposing` | تلفیقی | Consolidated Financial Statement | Consolidated financial statement | Parent-company / separate financial statement | business-owner-confirmed / confirmed |

The product owner confirmed `IsAudited=false` means unaudited, `IsRepresented=false` means not restated, and `IsComposing=false` means parent-company/separate; each flag is independent. These definitions apply to the Feature 141 generic path. No audited/restated default is inferred.

## Generic statement selection policy

Default to `IsComposing=false`; use true only when consolidated statements are requested. Return separate values for comparisons and never merge scopes. If separate data is unavailable, do not substitute consolidated data. This policy does not change CodalDb selection behavior.

## Financial statement providers and Codal selection review

No global provider precedence is implemented. `NoavaranCurrentApi` requests balance sheet, income statement, and cash flow endpoint families; `CyclicalWaves` currently normalizes quarterly income-statement fields only and carries stale-period warnings; `CodalDb` uses its own source-item normalization and selection policy. Preserve provider and selected revision separately.
Implemented Codal order: Within each (PeriodEnd.Date, PeriodType) group: IsAudited=true first; IsRepresented=true second; IsComposing=true by default (preferConsolidated=true); then StmtId ascending. When preferConsolidated=false only the IsComposing ordering reverses; audited and represented ordering is unchanged.
Conflict: Feature 141 defaults to IsComposing=false; CodalDb currently prefers IsComposing=true by default. This phase records the conflict only.
Selection review: The preference for restated variants and StmtId tie-break still require business review before generic reads rely on them. The generic Feature 141 default is IsComposing=false, which conflicts with current CodalDb preferConsolidated=true behavior. No code sorting was changed.

## Selected physical objects

### `public.Companies`

**English:** Novaran API company catalog rows with optional canonical market, industry, and group links, distinct from TSETMC-derived TradingInstruments.  
**Persian:** ردیف‌های فهرست شرکت‌ها به تفکیک منبع، با پیوندهای اختیاری به بازار، صنعت و گروه.  
**Purpose/grain:** Company identity and Novaran-sourced company dimensions; use the canonical UUID Id for supported relational joins. One provider/company identity row; ProviderName + ExternalCompanyId is unique in the live schema.

**Important columns:** `Id` — Internal canonical company UUID; `ProviderName` — Source/provider namespace; `ExternalCompanyId` — Provider-specific company key; not Companies.Id; `InstrumentCode` — Optional Novaran-side code used by the current TSETMC instrument linker; not a physical FK; `Ticker/CompanySymbol` — Provider or normalized labels; not stable cross-provider join keys; `ReportingType` — Nullable integer vendor reporting category; `MarketId/IndustryId/GroupId` — Optional physical FKs to reference dimensions

**Units:** ReportingType is an integer code; other identifiers are keys, not measures.

**Relationships:** Companies.MarketId -> Markets.Id (physical FK); Companies.IndustryId -> Industries.Id (physical FK); Companies.GroupId -> IndustryGroups.Id (physical FK); TradingInstruments.NormalizedCompanyId -> Companies.Id is a distinct optional FK populated by instrument-code resolution.

**Provider and period policy:** Business-owner-confirmed lineage: TSETMC -> Novaran Amin -> Novaran API -> Financial Copilot Companies. ProviderName distinguishes source namespaces and ExternalCompanyId is scoped to that provider. Do not join companies to TSETMC instruments by ticker/name; use only the explicit optional NormalizedCompanyId mapping and retain unresolved or ambiguous state. Company attributes are mutable catalog state; source synchronization timestamps identify freshness.

**Feature references:** None

**Questions:** شرکت‌های صنعت فلزات اساسی را فهرست کن; List companies in a market or industry

**Cautions:** NationalId and registration attributes are outside the initial read scope.; ReportingType business labels are owner-supplied; resolver support is separately documented.

**Evidence:** PostgreSQL/EF Core mapping and Noavaran API normalizer; lineage confirmed by product owner. Current instrument-code resolver behavior is separately documented under TradingInstruments.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.TradingInstruments`

**English:** TSETMC-derived reference dimension for securities and instrument identities used by normalized trade and index observations; identity is separate from Companies.  
**Persian:** بُعد مرجع ابزارهای معاملاتی برگرفته از TSETMC برای هویت ابزارهای به‌کاررفته در داده‌های معامله و شاخص؛ هویت آن از شرکت جداست.  
**Purpose/grain:** Resolve TSETMC instrument identifiers for market observations and optionally link an instrument to a canonical Companies.Id when the application mapping resolves it. One provider + ExternalInstrumentId row; ProviderName + ExternalInstrumentId and ProviderName + InstrumentCode are unique in PostgreSQL.

**Important columns:** `Id` — Internal Financial Copilot instrument UUID; referenced by market observations; `ProviderName` — Instrument-source namespace (current direct feed uses TsetmcWebService); `ExternalInstrumentId` — Deterministic UUID derived by the current TSETMC adapter from source InsCode; not the Companies UUID and not the source ISIN; `InstrumentCode` — TSETMC InsCode stored as bigint; source instrument key; `InstrumentIsin` — TSETMC InstrumentID (ISIN-like source identifier), kept separately from InsCode; `Symbol/Name` — Source ticker/company-name labels; descriptive, not cross-provider identity keys; `MarketCode/InstrumentKind` — TSETMC source classifications; code dictionaries are not included in this catalog; `NormalizedCompanyId` — Nullable FK to Companies.Id (canonical company UUID); `IsActive` — Source Valid flag; `SourceChangedAt/LastSynchronizedAt` — Current TSETMC adapter sets both to its synchronization time; they are not preserved vendor change timestamps

**Units:** Identifiers and labels, not financial measures. Market values and prices are stored on linked observations; their numeric PostgreSQL types do not encode display unit or scaling.

**Relationships:** NormalizedCompanyId -> Companies.Id is an optional physical FK with ON DELETE SET NULL; this is not an ExternalCompanyId join.; IntradayTradeSnapshots.TradingInstrumentId -> TradingInstruments.Id is a required physical FK.; DailyInstrumentTrades.TradingInstrumentId -> TradingInstruments.Id is a required physical FK.; IntradayIndexSnapshots.TradingInstrumentId -> TradingInstruments.Id is a required physical FK.; DailyIndexSnapshots.TradingInstrumentId -> TradingInstruments.Id is a required physical FK.

**Provider and period policy:** Product-owner-confirmed historical lineage: TSETMC -> direct TSETMC ingestion / historical migration -> Financial Copilot TradingInstruments. Current code also registers TsetmcDirectFeedSyncService; TsetmcPollingWorker schedules instrument sync at end of day when its worker flag and adapter operational settings are enabled. StockMarketDbSyncService currently reads its instrument dataset but deliberately does not write TradingInstruments. ProviderName must be retained. Mutable reference state; the current TSETMC adapter sets SourceChangedAt and LastSynchronizedAt to the synchronization timestamp, not a vendor change timestamp. Scheduled refresh is end-of-day when worker and adapter settings enable it; these fields do not prove every instrument was refreshed. Market snapshots carry their own exchange date/time and source receipt/observation time.

**Feature references:** Supports instrument identity for existing quote, daily-trade, and index observation paths; it does not establish company-level metric semantics.

**Questions:** نمایش سابقه قیمت یک ابزار معاملاتی با شناسه ابزار; Show trade or index history for a known TradingInstrumentId

**Cautions:** InsCode is not a database column: it maps to InstrumentCode; InstrumentId (ISIN-like) maps to InstrumentIsin; ExternalInstrumentId is a deterministic adapter GUID derived from InsCode.; The current company linker matches InsCode text to Companies.InstrumentCode, chooses the most recently synchronized match then Id, and does not filter Companies.ProviderName. InstrumentCode has no unique constraint on Companies; preserve this as a possible ambiguous/cross-provider mapping and do not replace it with ticker/name matching.; Market ingestion can create minimal instrument stub rows for unknown InsCode values; labels/type/ISIN and company link may remain incomplete until an instruments refresh.; Configured TSETMC instrument flows are documented as equity/ETF/rights; no reviewed InstrumentKind dictionary proves a full security/index taxonomy. Index observations still reference this dimension but index classification must use verified source mappings.; No source conversion is applied by the current normalizers; observation numeric scales must be checked against each provider/source mapping. Do not infer completeness from the most recent timestamp.

**Evidence:** Live read-only PostgreSQL pg_catalog snapshot and EF Core model; TsetmcDirectFeedSyncService, TsetmcPollingWorker, StockMarketDbSyncService, and provider DTO mappings; historical lineage confirmed by product owner.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.Markets`

**English:** Provider-scoped market reference dimension.  
**Persian:** بُعد مرجع بازار به تفکیک ارائه‌دهنده.  
**Purpose/grain:** Label a company’s market through Companies.MarketId. One provider market row.

**Important columns:** `Id` — Canonical market UUID; `ExternalId` — Provider market key; `Name` — Market label; `ProviderName` — Source namespace; `LastSynchronizedAt` — Last catalog synchronization time

**Units:** Identifiers and labels; no financial units.

**Relationships:** Companies.MarketId -> Markets.Id (physical FK; nullable on company)

**Provider and period policy:** Keep provider provenance for external identifiers. Current reference state.

**Feature references:** None

**Questions:** شرکت‌های بازار بورس را فهرست کن; List companies by market

**Cautions:** Market labels can vary by provider; do not infer cross-provider equivalence from names alone.

**Evidence:** EF Core mapping and live PostgreSQL FK.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.Industries`

**English:** Provider-scoped industry hierarchy.  
**Persian:** درخت صنایع به تفکیک ارائه‌دهنده.  
**Purpose/grain:** Company industry classification and optional parent hierarchy. One provider industry node.

**Important columns:** `Id` — Canonical UUID; `ExternalId` — Provider industry key; `Name` — Industry label; `ParentId` — Optional self-reference to Industries.Id; `ProviderName` — Source namespace

**Units:** Identifiers and labels; no financial units.

**Relationships:** Companies.IndustryId -> Industries.Id (physical FK); Industries.ParentId -> Industries.Id (physical self-FK)

**Provider and period policy:** Industry is source classification; do not infer SIC-like cross-provider categories. Current reference state.

**Feature references:** None

**Questions:** شرکت‌های یک صنعت را فهرست کن; List companies in an industry

**Cautions:** IndustryGroup is a separate dimension; its relation to Industries is not physically declared.

**Evidence:** EF Core mapping and live PostgreSQL FKs.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.IndustryGroups`

**English:** Provider-scoped industry-group lookup dimension.  
**Persian:** بُعد مرجع گروه صنعت به تفکیک ارائه‌دهنده.  
**Purpose/grain:** Group label reached from Companies.GroupId. One provider industry-group row.

**Important columns:** `Id` — Canonical UUID; `ExternalId` — Provider group key; `Name` — Group label; `ProviderName` — Source namespace

**Units:** Identifiers and labels; no financial units.

**Relationships:** Companies.GroupId -> IndustryGroups.Id (physical FK)

**Provider and period policy:** No direct FK from IndustryGroups to Industries is present. Current reference state.

**Feature references:** None

**Questions:** شرکت‌ها بر اساس گروه صنعت; Group companies by industry group

**Cautions:** Do not infer a group-to-industry path without an explicit mapping.

**Evidence:** EF Core mapping and live PostgreSQL FK inventory.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.MonthlyReports`

**English:** Provider-scoped monthly disclosure header with period, report category, output type, acceptance, and revision provenance.  
**Persian:** سرآیند گزارش ماهانه به تفکیک منبع، دوره، نوع گزارش، نوع خروجی، پذیرش و نسخه.  
**Purpose/grain:** Owns provider/report identity and accepted revision policy for monthly line items. One provider + external company + logical period + report type/output type + revision; accepted logical-period uniqueness is indexed.

**Important columns:** `Id` — Report UUID; `CompanyId` — Nullable FK to Companies.Id; `ExternalCompanyId` — Provider company identifier; not Companies.Id; `ProviderName` — Source/provider; `ExternalReportId` — Provider report identifier; `PeriodStart/PeriodEnd` — Normalized reporting interval; `ReportType/OutputType` — Report family and report view; `IsAccepted/RevisionStatus/RevisionFingerprint` — Accepted revision state and identity; `SourcePayloadChecksum` — Raw payload provenance; `VendorPeriodDate` — Vendor-supplied period date when parsed

**Units:** Dates and identifiers; no monetary measure in header.

**Relationships:** CompanyId -> Companies.Id (physical FK); MonthlyReportLineItems.MonthlyReportId -> MonthlyReports.Id is the application join; no physical FK was found in the live schema.

**Provider and period policy:** Filter provider and accepted logical revision. NoavaranCurrentApi monthly actuals use ProductSales/OutputType=0 for the governed single-month source; service route is distinct. CyclicalWaves is a separate provider and must not be merged by external ID alone. Normalized closed report period; VendorPeriodDate retains vendor period date when available.

**Feature references:** Feature 015 metric registry governs monthly sales and production calculations; do not duplicate its formulas.

**Questions:** آخرین گزارش ماهانه یک شرکت; Show accepted monthly reports for a company

**Cautions:** ExternalCompanyId is provider-scoped and has no FK to Companies.Id.; Do not select MAX(PeriodEnd) without provider, ReportType, OutputType, IsAccepted, and revision policy.

**Evidence:** Live PostgreSQL schema/indexes, Noavaran route client, monthly normalizers, and Feature 136 revision policy.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.MonthlyReportLineItems`

**English:** Product- or service-scoped monthly activity measures attached to a provider report by MonthlyReportId.  
**Persian:** اقلام فعالیت ماهانه محصول یا خدمت که با شناسه گزارش به سرآیند متصل می‌شوند.  
**Purpose/grain:** Persist production, sales quantity, sales amount, and provider-reported sales rate at source-line grain. One provider report source row/fingerprint, retaining company/provider product identity and multiplicity.

**Important columns:** `MonthlyReportId` — Logical join to MonthlyReports.Id (not enforced by physical FK); `ProviderProductId/ProviderProductCode/ProductCode` — Provider and normalized product keys; `Title` — Provider product/service name; `ProductionQuantity` — Reported production quantity; `SalesQuantity` — Reported sales quantity; `SalesAmount` — Reported amount; source/app convention is million IRR where verified; `SalesRate` — Stored/provider-derived rate; unit follows amount divided by quantity; `Unit` — Source quantity unit; `SourceMultiplicity/SourceRowFingerprint/SourceRowKey` — Duplicate and source-row provenance

**Units:** Quantity uses the row’s Unit. SalesAmount is in million IRR per the owner-supplied definition and trend persistence contract. The line-level SalesRate is a stored source/provider value where present; its implied unit is million IRR per source quantity unit. The separate governed MonthlyAverageSalesRate snapshot is total monthly SalesAmount divided by total SalesQuantity for selected product rows, only when units are not mixed and total quantity is nonzero. It is null when no usable quantity exists. Preserve negative source values; do not substitute the snapshot formula for a source-line SalesRate.

**Relationships:** MonthlyReportId -> MonthlyReports.Id is a logical/application join; no live FK constraint exists.

**Provider and period policy:** Use accepted header with the matching provider, report type and output type. Never merge products by title across companies/providers. Period comes from parent MonthlyReports; output type 0 represents single-month actuals in the governed Noavaran path.

**Feature references:** Feature 015 monthly sales/production metric definitions; Feature 076/136 governed trend and revision behavior.

**Questions:** فروش ماهانه محصول شرکت X چقدر بود؟; Show product sales quantity and amount for one accepted report

**Cautions:** No physical FK enforces MonthlyReportId.; Do not compare quantities across units. Mixed product units invalidate a generic aggregate.; Do not coerce null, zero, or negative amounts/quantities; preserve source values and apply approved metric policy.

**Evidence:** EF Core mappings, live PostgreSQL schema, normalizer source, and persisted trend contract; million-IRR scale also supplied by business owner.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.FinancialStatements`

**English:** Provider-scoped financial statement header and reporting-period/revision attributes.  
**Persian:** سرآیند صورت مالی به تفکیک ارائه‌دهنده و ویژگی‌های دوره و نسخه.  
**Purpose/grain:** Groups normalized statement line items by company, statement type, and fiscal period. One provider statement identity and period; provider-specific logical revisions may coexist.

**Important columns:** `Id` — Statement UUID; `CompanyId` — Nullable FK to Companies.Id; `ExternalCompanyId` — Provider company key; `ProviderName` — Source/provider; `ExternalStatementId` — Provider statement key; `StatementType` — BalanceSheet, IncomeStatement, or CashFlow; `PeriodType/PeriodStart/PeriodEnd` — Period duration and Gregorian bounds; `IsAudited` — false=Unaudited, true=Audited; business-owner-confirmed; `IsRepresented` — false=Not restated, true=Restated; business-owner-confirmed; `IsComposing` — false=Parent-company/separate, true=consolidated; business-owner-confirmed; `PublishedAt/VendorPeriodDate` — Disclosure and vendor period dates; `WarningsJson/SourcePayloadChecksum` — Quality and payload provenance

**Units:** Header contains period flags/dates; value units are on source-item definitions/metric registry, not the header.

**Relationships:** CompanyId -> Companies.Id (physical FK); FinancialStatementLineItems.FinancialStatementId is the application association; live schema does not enforce this FK.

**Provider and period policy:** No global provider precedence rule is implemented. NoavaranCurrentApi fetches BalanceSheet, IncomeStatement, and CashFlow endpoint families; CyclicalWaves normalizes quarterly income statement fields only; CodalDb selects one variant per (PeriodEnd.Date, PeriodType), ordered IsAudited=true first, then IsRepresented=true (Restated) first, then IsComposing=true by default (or false when preferConsolidated=false), then StmtId ascending. Provider is distinct from selected statement revision; explicitly select provider and preserve provenance. PeriodType is duration (e.g. ThreeMonths), distinct from StatementType. IsRepresented=true denotes Restated per the business owner; the physical column is unchanged and there is no separate IsRestated column. Provider revision identity remains separate from this semantic flag.

**Business definitions:**

- `IsAudited`: Audited Financial Statement / حسابرسی‌شده; true means Audited; false means Unaudited; provenance `business-owner-confirmed`; status `confirmed`.
- `IsRepresented`: Restated Financial Statement / تجدید ارائه‌شده; true means Restated; false means Not restated; provenance `business-owner-confirmed`; status `confirmed`.
- `IsComposing`: Consolidated Financial Statement / تلفیقی; true means Consolidated financial statement; false means Parent-company / separate financial statement; provenance `business-owner-confirmed`; status `confirmed`.

**Feature references:** Feature 015 financial metric definitions and unit governance are authoritative.

**Questions:** فروش و سود خالص فصل اخیر از صورت سود و زیان; Show audited income statement values for a fiscal period

**Cautions:** IsComposing is independent of audit and restatement flags.; CodalDb currently prefers IsComposing=true by default, which conflicts with Feature 141's generic-path default; the provider-specific behavior is unchanged.; CodalDb restatement preference and StmtId tie-break have no additional business approval in this phase.; CyclicalWaves period dates can be estimated and carry stale-data warnings.; Do not infer latest accepted revision by MAX(PeriodEnd) alone.

**Evidence:** Live PostgreSQL schema; Noavaran client; CyclicalWaves and CodalDb normalizers; CodalDb statement-selection policy.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.FinancialStatementSourceItems`

**English:** Provider-specific source-item catalog defining statement line identities, labels, and source units.  
**Persian:** فرهنگ اقلام مبدأ صورت مالی شامل شناسه ارائه‌دهنده، عنوان و واحد مبدأ.  
**Purpose/grain:** Provides canonical catalog IDs for line items and mappings to governed metric codes. One provider + statement type + provider SourceItemId definition.

**Important columns:** `Id` — Internal catalog UUID; `SourceItemId` — Provider-defined source item integer; not the row UUID; `ProviderName` — Provider namespace; `StatementType` — Applicable statement family; `TitleFa/TitleEn` — Localized labels; `Unit` — Source unit declaration

**Units:** Unit is provider/source definition metadata; normalize monetary and per-share units per Feature 015.

**Relationships:** FinancialStatementLineItems.SourceItemCatalogId -> FinancialStatementSourceItems.Id (physical FK); FinancialStatementSourceItemMetricMappings.SourceItemCatalogId -> FinancialStatementSourceItems.Id (physical FK)

**Provider and period policy:** Never join by SourceItemId alone across providers; join by catalog UUID or provider + source key. Definition rows are not period observations.

**Feature references:** FinancialStatementSourceItemMetricMappings.MetricCode -> Feature 015 metric registry identity.

**Questions:** کدام قلم مبدأ متناظر با درآمد عملیاتی است؟; Find the provider source item mapped to net profit

**Cautions:** One provider’s title or source ID does not imply equivalent meaning at another provider.

**Evidence:** EF Core mappings, live PostgreSQL FK inventory, and normalizer/metric mapping code.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.FinancialStatementLineItems`

**English:** Normalized statement values keyed to a metric code, statement row, and optional provider source-item definition.  
**Persian:** مقادیر نرمال‌شده اقلام صورت مالی با کد معیار و پیوند اختیاری به قلم مبدأ.  
**Purpose/grain:** Stores statement-level metric observations before governed derived-metric recalculation. One FinancialStatementId + MetricCode observation in application logic.

**Important columns:** `FinancialStatementId` — Application link to statement header; no live FK found; `MetricCode` — Metric identity, including provider codes where applicable; `Value` — Numeric source/normalized observation; `SourceItemCatalogId` — Optional FK to source-item catalog

**Units:** Values can be million IRR for monetary statement items, IRR per share for EPS-like values, ratios, or percent; use source catalog Unit plus Feature 015 registry. Do not assume all values share a scale.

**Relationships:** SourceItemCatalogId -> FinancialStatementSourceItems.Id (physical FK); FinancialStatementId -> FinancialStatements.Id is expected application key but not a live FK.

**Provider and period policy:** Provider-specific normalization and selection only; do not coalesce duplicate provider values without the existing selection rules. Inherits statement header period.

**Feature references:** MetricCode and unit governance come from Feature 015; avoid duplicating formulas.

**Questions:** درآمد صورت مالی فصل اخیر; Show net profit from a selected statement period

**Cautions:** Live inventory reports a uniqueness mismatch for (FinancialStatementId, MetricCode): EF expects unique, while PostgreSQL index is non-unique.; Per-share IRR must remain distinct from million-IRR statement values.

**Evidence:** Live PostgreSQL/EF reconciliation, Feature 015 registry, provider normalizers.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.FinancialStatementSourceItemMetricMappings`

**English:** Mapping from one provider source-item definition to a governed MetricCode.  
**Persian:** نگاشت قلم مبدأ ارائه‌دهنده به کد معیار حاکم.  
**Purpose/grain:** Connects provider-specific line items to the normalized financial metric registry. One source-item catalog UUID to one metric mapping; EF declares SourceItemCatalogId unique.

**Important columns:** `Id` — Mapping UUID; `SourceItemCatalogId` — FK to FinancialStatementSourceItems.Id; `MetricCode` — Governed normalized metric identity

**Units:** Units are inherited from source item and governed metric definition.

**Relationships:** SourceItemCatalogId -> FinancialStatementSourceItems.Id (physical FK)

**Provider and period policy:** Source item catalog identity is provider-specific; mapping is not evidence of provider precedence. Static mapping, not a dated observation.

**Feature references:** MetricCode -> Feature 015 financial metric catalog.

**Questions:** Map provider line items to normalized financial metrics

**Cautions:** Do not infer a one-to-one meaning across providers from matching MetricCode alone.

**Evidence:** EF configuration, live PostgreSQL FK, and metric registry.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.DerivedMetrics`

**English:** Versioned per-company, per-metric, per-period values with calculation policy and source/dependency evidence.  
**Persian:** مقادیر معیار نسخه‌دار برای شرکت و دوره، همراه با سیاست محاسبه و شواهد منبع و وابستگی.  
**Purpose/grain:** Persisted output of the governed calculation pipeline and selected externally supplied mapped values. ExternalCompanyId + MetricCode + MetricVersion + CalculationPolicyVersion + PeriodEnd (application upsert key).

**Important columns:** `ExternalCompanyId` — Provider-facing company identity; no Companies FK; `MetricCode` — Governed metric identity; `MetricVersion/CalculationPolicyVersion` — Semantic and formula versions; `PeriodType/PeriodStart/PeriodEnd` — Observation period; `Value/Unit` — Numeric observation and unit key; `SourceEvidenceJson/DependencyEvidenceJson` — Input provenance and calculation dependencies; `ObservedAt/LastSynchronizedAt` — Observation and ingestion timestamps

**Units:** Use Unit and Feature 015 metric definition; values include unit-dependent monetary, per-share, ratio, and percentage measures.

**Relationships:** No physical FK to Companies; resolve ExternalCompanyId only with matching provider context/source evidence.

**Provider and period policy:** CyclicalWaves statement/monthly data is normalized first and then recalculated through the provider-agnostic metric worker; it does not directly write DerivedMetrics in its normalizers. Noavaran fundamental-index mapped/vendor-precomputed values are separately normalized into DerivedMetrics with VendorPrecomputed evidence. Preserve source provenance; no provider precedence is implied. Versioned metric observation period, not necessarily a latest-market snapshot.

**Feature references:** Feature 015 is canonical for metric identity, units, formulas, and dependencies.

**Questions:** مقادیر معیارهای مالی با منبع و دوره مشخص; Show a governed metric with its calculation version and evidence

**Cautions:** ExternalCompanyId alone is not globally unique.; Do not recompute or reinterpret values without the registered metric/policy version.

**Evidence:** DerivedMetricResultStore, MetricRecalculationProcessor, NoavaranFundamentalIndexNormalizer, and CyclicalWaves persistence audit.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.CompanyIndustryRelativeValuations`

**English:** Company rows from a versioned industry-relative valuation calculation, including PE, PS, equilibrium, validity, classification, and rank results.  
**Persian:** نتایج ارزش‌گذاری نسبی شرکت در صنعت شامل P/E، P/S، قیمت تعادلی، اعتبار، طبقه‌بندی و رتبه نسخه‌دار.  
**Purpose/grain:** Persist a governed valuation calculation snapshot and its source-observation provenance. One company per IndustryRelativeValuationCalculations.CalculationId; ranking is scoped to that calculation/version.

**Important columns:** `CompanyId` — FK to Companies.Id; `CalculationId` — FK to calculation header; `CurrentPE/CurrentPS/HistoricalAveragePE/HistoricalAveragePS` — Inputs and comparison values; `PEPercent/PSPercent/EquilibriumPercent` — Relative deviations; `PEClassification/PSClassification/EquilibriumClassification` — Classification outputs; `IsValid/IsOutlier/Reason` — Quality flags and explanations; `GlobalRank/RankVersion` — Persisted rank and ranking version; `SourceFactId/ObservationId/Timestamp/Version/Watermark` — Source lineage per result family

**Units:** PE/PS are ratios; prices use the registered market-price unit; percentages are percent values as defined by calculator.

**Relationships:** CompanyId -> Companies.Id (physical FK); CalculationId -> IndustryRelativeValuationCalculations.Id (physical FK)

**Provider and period policy:** Consume complete calculation snapshot and its source provenance; canonical provider membership and source fact identity/freshness are enforced by Feature 125 calculation orchestration. A valid IQR outlier is excluded from the benchmark and is Unclassifiable; do not recompute GlobalRank. Calculation snapshot timestamp/source observation watermarks; not a fiscal statement period.

**Feature references:** Feature 125 engine IQR-R7-1.5-v1 normalizes positive current/reference pairs as current ÷ reference × 100; benchmarks per group/metric with R-7 quartiles and 1.5×IQR bounds; requires at least two clean observations; classifies at/below clean average Green and above it Red; ranks by Green count, then PE/PS/equilibrium percentages, valid count, and CompanyId. Not a generic table formula.

**Questions:** رتبه نسبی P/E شرکت در صنعت; Show the persisted industry-relative valuation rank

**Cautions:** GlobalRank is versioned calculation output and must not be recomputed by sorting raw fields.; Invalid/outlier values and calculation freshness must be preserved.

**Evidence:** EF/live FKs and Feature 125 IndustryRelativeValuationEngine IQR-R7-1.5-v1, calculation input builder, snapshot writer, and orchestration.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.CompanyMonthlyActivityTrendSnapshots`

**English:** Persisted company-month activity trend projection with governed comparisons, completeness, output types, and source provenance.  
**Persian:** نمای روند فعالیت ماهانه شرکت با مقایسه‌های حاکم، کامل‌بودن داده، نوع خروجی و منشأ منبع.  
**Purpose/grain:** Chart-ready monthly production/sales trend responses; the AI path reads snapshots instead of aggregating raw line items at request time. Company external identity + report year/month and governed output-type selections.

**Important columns:** `ExternalCompanyId` — Provider-facing company key; `ReportYear/ReportMonth` — Reported month; `MonthlySalesAmount/MonthlyProductionQuantity/MonthlySalesQuantity` — Persisted monthly totals; `MonthlyAverageSalesRate` — Governed average rate; `SameMonthPreviousYear*` — Prior-year comparison measures; `Average12Month*` — Rolling average and period count; `Ytd*` — Year-to-date values; `*GrowthPercent` — Governed period comparisons; `CurrentMonthOutputType/YtdOutputType/YtdPreviousMonthOutputType` — Source output-type policies; `HasMixedProductUnits/ProductUnitSummary/DataCompletenessScore` — Quality/comparability state; `SourceProviderName/SourceReportId/SourceRawPayloadId` — Provenance; `CalculatedAtUtc` — Snapshot calculation timestamp

**Units:** Persisted SalesAmount is million IRR; product quantities retain provider units; response layer converts amount to billion toman (value × 0.0001) where that contract applies. Mixed units are flagged.

**Relationships:** ExternalCompanyId has no physical FK to Companies; resolver joins must use provider identity policy.

**Provider and period policy:** Respect SourceProviderName, output-type fields, data completeness, and existing specialized response policy. ReportYear/ReportMonth are report month; calendar/fiscal fields are separate. CalculatedAtUtc is not report period end.

**Feature references:** Feature 076/077/136 trend snapshot and response policy is authoritative; do not create raw-line generic trend calculations.

**Questions:** روند فروش ماهانه شرکت X; Compare monthly sales with the same month last year

**Cautions:** Mixed product units make aggregate quantities incomparable.; Persisted trend data may be stale or incomplete; do not equate latest date with complete coverage.

**Evidence:** Trend snapshot contracts/calculator/query path and README monetary conversion policy.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.IntradayTradeSnapshots`

**English:** Intraday instrument trade/quote observations from normalized market-data provider feeds.  
**Persian:** مشاهدات درون‌روزی معامله و قیمت برای ابزار معاملاتی از منابع نرمال‌شده داده بازار.  
**Purpose/grain:** Append-oriented market snapshot history for instrument-level intraday reads. One provider ExternalSnapshotId for one TradingInstrument at TradingDate + TradingTime; ReceivedAt records ingestion receipt.

**Important columns:** `TradingInstrumentId` — FK to TradingInstruments.Id, not Companies.Id; `TradingDate/TradingTime` — Exchange-local observation date/time fields (time has no time zone); `LastTradedPrice/ClosingPrice/PriceChange/PriceYesterday` — Price fields; `Volume/TotalTransactions/TotalCapital` — Market activity fields; `ProviderName/ExternalSnapshotId` — Source identity; `ReceivedAt` — Ingestion receipt timestamp

**Units:** Prices and market activity are numeric source values copied by the adapters; PostgreSQL stores no unit/scale metadata and no universal conversion is applied. Verify ProviderName and source mapping before presentation; do not combine provider scales by column name alone.

**Relationships:** TradingInstrumentId -> TradingInstruments.Id (physical FK); TradingInstruments.NormalizedCompanyId -> Companies.Id is optional; no direct company FK.

**Provider and period policy:** ProviderName and ExternalSnapshotId identify each source row. The direct TSETMC adapter persists TSE TradeLastDay observations; StockMarketDB is a separate ingestion path. Repository worker defaults enable TSETMC polling and disable StockMarketDB polling, but deployment overrides/current runtime state are not certified here. Intraday point observations; trading date/time are not UTC timestamps. ReceivedAt is a separate timestamp with time zone.

**Feature references:** Existing market quote projection owns latest-price semantics.

**Questions:** آخرین قیمت ثبت‌شده درون‌روزی نماد; Show intraday price snapshots for an instrument

**Cautions:** Polling gaps, retention, and market calendar affect completeness.; MAX(TradingDate/TradingTime) does not prove a complete/current market feed.

**Evidence:** TsetmcDirectFeedSyncService and StockMarketDbSyncService mappings, worker defaults, live PostgreSQL FK/type metadata.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.DailyInstrumentTrades`

**English:** One normalized daily instrument trade/price observation per source trading date and instrument.  
**Persian:** مشاهده روزانه قیمت و معامله برای هر ابزار در تاریخ معاملاتی منبع.  
**Purpose/grain:** Daily historical observations distinct from intraday snapshots. TradingInstrument + TradingDate under provider daily-trade identity policy.

**Important columns:** `TradingInstrumentId` — FK to TradingInstruments.Id; `TradingDate` — Source trading date; `ClosingPrice/LastTradedPrice/PriceChange/PriceYesterday` — Daily price fields; `Volume/MarketValue/TotalCapital/TotalTransactions` — Daily activity fields; `ExternalTradeId` — Source trade-row GUID; `SourceInsertedAt` — Source insertion timestamp

**Units:** Price, MarketValue, and volume are numeric source values copied by the adapters; PostgreSQL stores no unit/scale metadata and no universal conversion is applied. Verify ProviderName and source mapping before presentation; do not combine provider scales by column name alone.

**Relationships:** TradingInstrumentId -> TradingInstruments.Id (physical FK); optional instrument-to-company relationship through NormalizedCompanyId.

**Provider and period policy:** ProviderName and ExternalTradeId identify the normalized row. The direct TSETMC adapter uses daily trade history; StockMarketDB has a distinct TradeRefined path. Retain source provenance and avoid cross-provider deduplication by date/instrument alone. Daily trading-calendar date, not necessarily Gregorian calendar day coverage.

**Feature references:** Daily observation; not a latest quote projection or an intraday event.

**Questions:** قیمت پایانی روز معاملاتی نماد; Show daily close and volume for a trading date

**Cautions:** Non-trading days have no row; check source completeness/watermark.; Do not infer currentness from max date alone.

**Evidence:** TsetmcDirectFeedSyncService and StockMarketDbSyncService mappings; live PostgreSQL FK/type metadata.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.IntradayIndexSnapshots`

**English:** Intraday observations for market/index instruments.  
**Persian:** مشاهدات درون‌روزی برای ابزارهای شاخص بازار.  
**Purpose/grain:** Intraday index series; the instrument table defines index identity. Provider ExternalSnapshotId per TradingInstrumentId and TradingDate/TradingTime.

**Important columns:** `TradingInstrumentId` — FK to TradingInstruments.Id; `TradingDate/TradingTime` — Exchange-local date and timezone-free time; `Value/ChangePercent` — Index level and change measure; `ProviderName/ExternalSnapshotId` — Provider snapshot identity; `SourceChangedAt` — Source change timestamp

**Units:** Index level and percentage change follow source mapping; numeric columns do not encode unit.

**Relationships:** TradingInstrumentId -> TradingInstruments.Id (physical FK); no direct Companies relationship.

**Provider and period policy:** ProviderName and ExternalSnapshotId identify each observation. Direct TSETMC and StockMarketDB index adapters are separate sources; repository worker defaults enable TSETMC polling and disable StockMarketDB polling, subject to deployment overrides. Multiple observations within exchange trading date; SourceChangedAt is a separate source timestamp.

**Feature references:** None

**Questions:** روند درون‌روزی شاخص کل; Show intraday index levels for the trading session

**Cautions:** Snapshot polling is not guaranteed complete; max observation is not completeness proof.; Index identity is on TradingInstruments, not company identity.

**Evidence:** TsetmcDirectFeedSyncService and StockMarketDbSyncService mappings; live PostgreSQL FK/type metadata.

Approval: `candidate-pending-review`; queryable: `false`.

### `public.DailyIndexSnapshots`

**English:** Daily index observations keyed by instrument and trading date.  
**Persian:** مشاهدات روزانه شاخص با کلید ابزار و تاریخ معاملاتی.  
**Purpose/grain:** Daily historical index levels and close-derived observations. One TradingInstrumentId + TradingDate observation per provider policy.

**Important columns:** `TradingInstrumentId` — FK to TradingInstruments.Id; `TradingDate` — Trading-calendar date; `Value/High/Low/ChangePercent` — Daily level/range/change fields; `ProviderName/SourceKind` — Provider and derivation/source family; `ObservedAt` — Observation timestamp

**Units:** Index levels and percent change per source adapter; not a monetary company metric.

**Relationships:** TradingInstrumentId -> TradingInstruments.Id (physical FK).

**Provider and period policy:** ProviderName and SourceKind distinguish source history and derived rows. Direct TSETMC and StockMarketDB daily-index adapters are separate sources; retain SourceKind and source lineage, and do not merge by instrument/date alone. One daily observation per named index and trading date; trading date coverage follows exchange calendar.

**Feature references:** None

**Questions:** مقدار پایانی روزانه شاخص کل; Show daily index history for a trading period

**Cautions:** Historical source tables can be stale; use source-specific currentness policy.; Do not treat max TradingDate as complete/current without watermark and source semantics.

**Evidence:** TsetmcDirectFeedSyncService and StockMarketDbSyncService mappings; live PostgreSQL FK/type metadata.

Approval: `candidate-pending-review`; queryable: `false`.

## Additional external data source

`api/v2/CompanyFundamentalIndex/Values` is a Noavaran provider endpoint, not a local table or public Financial Copilot controller route. The raw provider payload is retained in `ProviderRawPayloads`; mapped, scale-verified observations are normalized into `DerivedMetrics` with provider and vendor-precomputed evidence. Coverage sync uses a distinct `FundamentalIndexCoverage` dataset. See `SemanticCatalog/sources/src_nadpco_company_fundamental_index_values.yaml`.

## Confirmed joins and unresolved relationships

- Physical FK: Companies -> Markets, Industries, IndustryGroups; Industries.ParentId -> Industries.Id.
- Physical FK: MonthlyReports.CompanyId -> Companies.Id; ExternalCompanyId is a provider-scoped text key. MonthlyReportLineItems.MonthlyReportId -> MonthlyReports.Id is not enforced by a live FK.
- Physical FK: FinancialStatements.CompanyId -> Companies.Id; FinancialStatementLineItems.FinancialStatementId -> FinancialStatements.Id is not enforced by a live FK in the captured PostgreSQL schema; SourceItemCatalogId does have an FK.
- Physical FK: SourceItemMetricMappings.SourceItemCatalogId -> FinancialStatementSourceItems.Id.
- Physical FK: CompanyIndustryRelativeValuations.CompanyId -> Companies.Id and CalculationId -> IndustryRelativeValuationCalculations.Id.
- Physical FK verified in PostgreSQL and EF: IntradayTradeSnapshots.TradingInstrumentId -> TradingInstruments.Id; required, ON DELETE CASCADE.
- Physical FK verified in PostgreSQL and EF: DailyInstrumentTrades.TradingInstrumentId -> TradingInstruments.Id; required, ON DELETE CASCADE.
- Physical FK verified in PostgreSQL and EF: IntradayIndexSnapshots.TradingInstrumentId -> TradingInstruments.Id; required, ON DELETE CASCADE.
- Physical FK verified in PostgreSQL and EF: DailyIndexSnapshots.TradingInstrumentId -> TradingInstruments.Id; required, ON DELETE CASCADE.
- Physical FK verified in PostgreSQL and EF: TradingInstruments.NormalizedCompanyId -> Companies.Id; optional, non-unique, ON DELETE SET NULL.
- TSETMC application mapping matches source InsCode text to Companies.InstrumentCode, then selects latest LastSynchronizedAt and Id; it does not filter Company.ProviderName and Companies.InstrumentCode is not unique. Treat the resulting link as potentially ambiguous/cross-provider; missing links remain null.
- DerivedMetrics and CompanyMonthlyActivityTrendSnapshots use ExternalCompanyId without a Companies FK; do not join globally without provider identity.
- FinancialStatementLineItems(FinancialStatementId, MetricCode) differs between EF uniqueness and live PostgreSQL index; resolve drift before exposing that grain.

## Industry-relative valuation policy

Algorithm `IQR-R7-1.5-v1`: CurrentValue / ReferenceValue * 100; only positive current and reference values with valid identity, available and fresh source facts are valid. Per canonical group and metric; R-7 Q1/Q3; Tukey 1.5*IQR bounds; outliers excluded; clean average available only with at least two clean observations. Valid non-outlier percent <= clean average is Green; above is Red. Outliers and unavailable benchmark cases are Unclassifiable; invalid non-positive input is Red. Per group: Green metric count descending; then PE, PS, Equilibrium percent ascending with missing values last; valid metric count descending; CompanyId ascending.
Evidence retained: IndustryRelativeValuationEngine, calculation input builder, and snapshot writer; snapshots retain algorithm version, membership hash, source barrier hash, and source observations.


## Proposed initial read scope

The proposal includes 18 selected physical tables plus the one Noavaran external endpoint record. It is not approved; all physical objects and the endpoint remain non-queryable. Prefer existing metric and trend read services where direct reads could bypass provider, revision, unit, completeness, or calculation policies.

### Existing support boundaries

- Existing governed path: company financial metrics already registered in Feature 015
- Existing governed path: monthly activity trends already persisted by Feature 076/077/136
- Existing governed path: instrument quote/daily history where the existing market read model has complete coverage
- Not approved for generic SQL: cross-company product category comparisons without reviewed aliases
- Not approved for generic SQL: raw monthly growth/rate recalculation
- Not approved for generic SQL: unselected provider statement joins
- Not approved for generic SQL: restatement comparisons until Codal selection policy and provider revision semantics receive human review
- Not approved for generic SQL: complete/current market claims based on maximum timestamp
- Not approved for generic SQL: direct raw payload queries
- Not approved for generic SQL: company-level market joins inferred from ticker or company name

## Remaining human review

- Review the owner-supplied Persian/English ReportingType labels and million-IRR sales-amount contract before granting generic query authorization.
- Confirm the CodalDb preference for restated statements and the `StmtId` ascending tie-break; current specialized behavior remains unchanged.
- Verify provider DTO/source mappings preserve the owner-confirmed IsComposing meaning for each source; the generic-path default itself is IsComposing=false.
- Review TSETMC instrument-to-company resolution collisions/provider scope and approve any future mapping-policy change separately.
- Review semantic labels, provider eligibility, accepted revision rules, market units/source scales, and example questions before granting any queryability.
- Resolve missing live FK constraints for monthly line items and statement line items, plus the EF/PostgreSQL uniqueness drift on statement line items.
- Review sample coverage and the observed future-dated monthly period before making currentness claims.
- Navigation labels and hierarchy grouping are pending review; their generated presence grants no semantic or runtime query approval.
