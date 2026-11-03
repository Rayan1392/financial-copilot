#!/usr/bin/env python3
"""Apply the source-reviewed Feature 141 candidate overlay and render its Markdown review."""

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
FEATURE = ROOT / "specs/141-schema-aware-semantic-query-engine"
CATALOG = FEATURE / "SemanticCatalog"
TABLES = CATALOG / "tables"
SCOPE = CATALOG / "catalog-scope.yaml"
SNAPSHOT = FEATURE / "artifacts/schema-snapshot.json"
MARKDOWN = FEATURE / "curated-catalog-review.md"


CURATIONS = {
    "Companies": {
        "descriptionEn": "Novaran API company catalog rows with optional canonical market, industry, and group links, distinct from TSETMC-derived TradingInstruments.",
        "descriptionFa": "ردیف‌های فهرست شرکت‌ها به تفکیک منبع، با پیوندهای اختیاری به بازار، صنعت و گروه.",
        "purpose": "Company identity and Novaran-sourced company dimensions; use the canonical UUID Id for supported relational joins.",
        "grain": "One provider/company identity row; ProviderName + ExternalCompanyId is unique in the live schema.",
        "columns": {"Id": "Internal canonical company UUID", "ProviderName": "Source/provider namespace", "ExternalCompanyId": "Provider-specific company key; not Companies.Id", "InstrumentCode": "Optional Novaran-side code used by the current TSETMC instrument linker; not a physical FK", "Ticker/CompanySymbol": "Provider or normalized labels; not stable cross-provider join keys", "ReportingType": "Nullable integer vendor reporting category", "MarketId/IndustryId/GroupId": "Optional physical FKs to reference dimensions"},
        "units": "ReportingType is an integer code; other identifiers are keys, not measures.",
        "relationships": ["Companies.MarketId -> Markets.Id (physical FK)", "Companies.IndustryId -> Industries.Id (physical FK)", "Companies.GroupId -> IndustryGroups.Id (physical FK)", "TradingInstruments.NormalizedCompanyId -> Companies.Id is a distinct optional FK populated by instrument-code resolution."],
        "metricReferences": [],
        "providerPolicy": "Business-owner-confirmed lineage: TSETMC -> Novaran Amin -> Novaran API -> Financial Copilot Companies. ProviderName distinguishes source namespaces and ExternalCompanyId is scoped to that provider. Do not join companies to TSETMC instruments by ticker/name; use only the explicit optional NormalizedCompanyId mapping and retain unresolved or ambiguous state.",
        "periodSemantics": "Company attributes are mutable catalog state; source synchronization timestamps identify freshness.",
        "examples": ["شرکت‌های صنعت فلزات اساسی را فهرست کن", "List companies in a market or industry"],
        "cautions": ["NationalId and registration attributes are outside the initial read scope.", "ReportingType business labels are owner-supplied; resolver support is separately documented."],
        "sourceVerification": "PostgreSQL/EF Core mapping and Noavaran API normalizer; lineage confirmed by product owner. Current instrument-code resolver behavior is separately documented under TradingInstruments.",
    },
    "TradingInstruments": {
        "descriptionEn": "TSETMC-derived reference dimension for securities and instrument identities used by normalized trade and index observations; identity is separate from Companies.",
        "descriptionFa": "بُعد مرجع ابزارهای معاملاتی برگرفته از TSETMC برای هویت ابزارهای به‌کاررفته در داده‌های معامله و شاخص؛ هویت آن از شرکت جداست.",
        "purpose": "Resolve TSETMC instrument identifiers for market observations and optionally link an instrument to a canonical Companies.Id when the application mapping resolves it.",
        "grain": "One provider + ExternalInstrumentId row; ProviderName + ExternalInstrumentId and ProviderName + InstrumentCode are unique in PostgreSQL.",
        "columns": {"Id": "Internal Financial Copilot instrument UUID; referenced by market observations", "ProviderName": "Instrument-source namespace (current direct feed uses TsetmcWebService)", "ExternalInstrumentId": "Deterministic UUID derived by the current TSETMC adapter from source InsCode; not the Companies UUID and not the source ISIN", "InstrumentCode": "TSETMC InsCode stored as bigint; source instrument key", "InstrumentIsin": "TSETMC InstrumentID (ISIN-like source identifier), kept separately from InsCode", "Symbol/Name": "Source ticker/company-name labels; descriptive, not cross-provider identity keys", "MarketCode/InstrumentKind": "TSETMC source classifications; code dictionaries are not included in this catalog", "NormalizedCompanyId": "Nullable FK to Companies.Id (canonical company UUID)", "IsActive": "Source Valid flag", "SourceChangedAt/LastSynchronizedAt": "Current TSETMC adapter sets both to its synchronization time; they are not preserved vendor change timestamps"},
        "units": "Identifiers and labels, not financial measures. Market values and prices are stored on linked observations; their numeric PostgreSQL types do not encode display unit or scaling.",
        "relationships": ["NormalizedCompanyId -> Companies.Id is an optional physical FK with ON DELETE SET NULL; this is not an ExternalCompanyId join.", "IntradayTradeSnapshots.TradingInstrumentId -> TradingInstruments.Id is a required physical FK.", "DailyInstrumentTrades.TradingInstrumentId -> TradingInstruments.Id is a required physical FK.", "IntradayIndexSnapshots.TradingInstrumentId -> TradingInstruments.Id is a required physical FK.", "DailyIndexSnapshots.TradingInstrumentId -> TradingInstruments.Id is a required physical FK."],
        "metricReferences": ["Supports instrument identity for existing quote, daily-trade, and index observation paths; it does not establish company-level metric semantics."],
        "providerPolicy": "Product-owner-confirmed historical lineage: TSETMC -> direct TSETMC ingestion / historical migration -> Financial Copilot TradingInstruments. Current code also registers TsetmcDirectFeedSyncService; TsetmcPollingWorker schedules instrument sync at end of day when its worker flag and adapter operational settings are enabled. StockMarketDbSyncService currently reads its instrument dataset but deliberately does not write TradingInstruments. ProviderName must be retained.",
        "periodSemantics": "Mutable reference state; the current TSETMC adapter sets SourceChangedAt and LastSynchronizedAt to the synchronization timestamp, not a vendor change timestamp. Scheduled refresh is end-of-day when worker and adapter settings enable it; these fields do not prove every instrument was refreshed. Market snapshots carry their own exchange date/time and source receipt/observation time.",
        "examples": ["نمایش سابقه قیمت یک ابزار معاملاتی با شناسه ابزار", "Show trade or index history for a known TradingInstrumentId"],
        "cautions": ["InsCode is not a database column: it maps to InstrumentCode; InstrumentId (ISIN-like) maps to InstrumentIsin; ExternalInstrumentId is a deterministic adapter GUID derived from InsCode.", "The current company linker matches InsCode text to Companies.InstrumentCode, chooses the most recently synchronized match then Id, and does not filter Companies.ProviderName. InstrumentCode has no unique constraint on Companies; preserve this as a possible ambiguous/cross-provider mapping and do not replace it with ticker/name matching.", "Market ingestion can create minimal instrument stub rows for unknown InsCode values; labels/type/ISIN and company link may remain incomplete until an instruments refresh.", "Configured TSETMC instrument flows are documented as equity/ETF/rights; no reviewed InstrumentKind dictionary proves a full security/index taxonomy. Index observations still reference this dimension but index classification must use verified source mappings.", "No source conversion is applied by the current normalizers; observation numeric scales must be checked against each provider/source mapping. Do not infer completeness from the most recent timestamp."],
        "sourceVerification": "Live read-only PostgreSQL pg_catalog snapshot and EF Core model; TsetmcDirectFeedSyncService, TsetmcPollingWorker, StockMarketDbSyncService, and provider DTO mappings; historical lineage confirmed by product owner.",
    },
    "Markets": {
        "descriptionEn": "Provider-scoped market reference dimension.", "descriptionFa": "بُعد مرجع بازار به تفکیک ارائه‌دهنده.",
        "purpose": "Label a company’s market through Companies.MarketId.", "grain": "One provider market row.",
        "columns": {"Id": "Canonical market UUID", "ExternalId": "Provider market key", "Name": "Market label", "ProviderName": "Source namespace", "LastSynchronizedAt": "Last catalog synchronization time"},
        "units": "Identifiers and labels; no financial units.", "relationships": ["Companies.MarketId -> Markets.Id (physical FK; nullable on company)"],
        "metricReferences": [], "providerPolicy": "Keep provider provenance for external identifiers.", "periodSemantics": "Current reference state.",
        "examples": ["شرکت‌های بازار بورس را فهرست کن", "List companies by market"], "cautions": ["Market labels can vary by provider; do not infer cross-provider equivalence from names alone."],
        "sourceVerification": "EF Core mapping and live PostgreSQL FK.",
    },
    "Industries": {
        "descriptionEn": "Provider-scoped industry hierarchy.", "descriptionFa": "درخت صنایع به تفکیک ارائه‌دهنده.",
        "purpose": "Company industry classification and optional parent hierarchy.", "grain": "One provider industry node.",
        "columns": {"Id": "Canonical UUID", "ExternalId": "Provider industry key", "Name": "Industry label", "ParentId": "Optional self-reference to Industries.Id", "ProviderName": "Source namespace"},
        "units": "Identifiers and labels; no financial units.", "relationships": ["Companies.IndustryId -> Industries.Id (physical FK)", "Industries.ParentId -> Industries.Id (physical self-FK)"],
        "metricReferences": [], "providerPolicy": "Industry is source classification; do not infer SIC-like cross-provider categories.", "periodSemantics": "Current reference state.",
        "examples": ["شرکت‌های یک صنعت را فهرست کن", "List companies in an industry"], "cautions": ["IndustryGroup is a separate dimension; its relation to Industries is not physically declared."],
        "sourceVerification": "EF Core mapping and live PostgreSQL FKs.",
    },
    "IndustryGroups": {
        "descriptionEn": "Provider-scoped industry-group lookup dimension.", "descriptionFa": "بُعد مرجع گروه صنعت به تفکیک ارائه‌دهنده.",
        "purpose": "Group label reached from Companies.GroupId.", "grain": "One provider industry-group row.",
        "columns": {"Id": "Canonical UUID", "ExternalId": "Provider group key", "Name": "Group label", "ProviderName": "Source namespace"},
        "units": "Identifiers and labels; no financial units.", "relationships": ["Companies.GroupId -> IndustryGroups.Id (physical FK)"],
        "metricReferences": [], "providerPolicy": "No direct FK from IndustryGroups to Industries is present.", "periodSemantics": "Current reference state.",
        "examples": ["شرکت‌ها بر اساس گروه صنعت", "Group companies by industry group"], "cautions": ["Do not infer a group-to-industry path without an explicit mapping."],
        "sourceVerification": "EF Core mapping and live PostgreSQL FK inventory.",
    },
    "MonthlyReports": {
        "descriptionEn": "Provider-scoped monthly disclosure header with period, report category, output type, acceptance, and revision provenance.",
        "descriptionFa": "سرآیند گزارش ماهانه به تفکیک منبع، دوره، نوع گزارش، نوع خروجی، پذیرش و نسخه.",
        "purpose": "Owns provider/report identity and accepted revision policy for monthly line items.",
        "grain": "One provider + external company + logical period + report type/output type + revision; accepted logical-period uniqueness is indexed.",
        "columns": {"Id": "Report UUID", "CompanyId": "Nullable FK to Companies.Id", "ExternalCompanyId": "Provider company identifier; not Companies.Id", "ProviderName": "Source/provider", "ExternalReportId": "Provider report identifier", "PeriodStart/PeriodEnd": "Normalized reporting interval", "ReportType/OutputType": "Report family and report view", "IsAccepted/RevisionStatus/RevisionFingerprint": "Accepted revision state and identity", "SourcePayloadChecksum": "Raw payload provenance", "VendorPeriodDate": "Vendor-supplied period date when parsed"},
        "units": "Dates and identifiers; no monetary measure in header.",
        "relationships": ["CompanyId -> Companies.Id (physical FK)", "MonthlyReportLineItems.MonthlyReportId -> MonthlyReports.Id is the application join; no physical FK was found in the live schema."],
        "metricReferences": ["Feature 015 metric registry governs monthly sales and production calculations; do not duplicate its formulas."],
        "providerPolicy": "Filter provider and accepted logical revision. NoavaranCurrentApi monthly actuals use ProductSales/OutputType=0 for the governed single-month source; service route is distinct. CyclicalWaves is a separate provider and must not be merged by external ID alone.",
        "periodSemantics": "Normalized closed report period; VendorPeriodDate retains vendor period date when available.",
        "examples": ["آخرین گزارش ماهانه یک شرکت", "Show accepted monthly reports for a company"],
        "cautions": ["ExternalCompanyId is provider-scoped and has no FK to Companies.Id.", "Do not select MAX(PeriodEnd) without provider, ReportType, OutputType, IsAccepted, and revision policy."],
        "sourceVerification": "Live PostgreSQL schema/indexes, Noavaran route client, monthly normalizers, and Feature 136 revision policy.",
    },
    "MonthlyReportLineItems": {
        "descriptionEn": "Product- or service-scoped monthly activity measures attached to a provider report by MonthlyReportId.",
        "descriptionFa": "اقلام فعالیت ماهانه محصول یا خدمت که با شناسه گزارش به سرآیند متصل می‌شوند.",
        "purpose": "Persist production, sales quantity, sales amount, and provider-reported sales rate at source-line grain.",
        "grain": "One provider report source row/fingerprint, retaining company/provider product identity and multiplicity.",
        "columns": {"MonthlyReportId": "Logical join to MonthlyReports.Id (not enforced by physical FK)", "ProviderProductId/ProviderProductCode/ProductCode": "Provider and normalized product keys", "Title": "Provider product/service name", "ProductionQuantity": "Reported production quantity", "SalesQuantity": "Reported sales quantity", "SalesAmount": "Reported amount; source/app convention is million IRR where verified", "SalesRate": "Stored/provider-derived rate; unit follows amount divided by quantity", "Unit": "Source quantity unit", "SourceMultiplicity/SourceRowFingerprint/SourceRowKey": "Duplicate and source-row provenance"},
        "units": "Quantity uses the row’s Unit. SalesAmount is in million IRR per the owner-supplied definition and trend persistence contract. The line-level SalesRate is a stored source/provider value where present; its implied unit is million IRR per source quantity unit. The separate governed MonthlyAverageSalesRate snapshot is total monthly SalesAmount divided by total SalesQuantity for selected product rows, only when units are not mixed and total quantity is nonzero. It is null when no usable quantity exists. Preserve negative source values; do not substitute the snapshot formula for a source-line SalesRate.",
        "relationships": ["MonthlyReportId -> MonthlyReports.Id is a logical/application join; no live FK constraint exists."],
        "metricReferences": ["Feature 015 monthly sales/production metric definitions; Feature 076/136 governed trend and revision behavior."],
        "providerPolicy": "Use accepted header with the matching provider, report type and output type. Never merge products by title across companies/providers.",
        "periodSemantics": "Period comes from parent MonthlyReports; output type 0 represents single-month actuals in the governed Noavaran path.",
        "examples": ["فروش ماهانه محصول شرکت X چقدر بود؟", "Show product sales quantity and amount for one accepted report"],
        "cautions": ["No physical FK enforces MonthlyReportId.", "Do not compare quantities across units. Mixed product units invalidate a generic aggregate.", "Do not coerce null, zero, or negative amounts/quantities; preserve source values and apply approved metric policy."],
        "sourceVerification": "EF Core mappings, live PostgreSQL schema, normalizer source, and persisted trend contract; million-IRR scale also supplied by business owner.",
    },
    "FinancialStatements": {
        "descriptionEn": "Provider-scoped financial statement header and reporting-period/revision attributes.",
        "descriptionFa": "سرآیند صورت مالی به تفکیک ارائه‌دهنده و ویژگی‌های دوره و نسخه.",
        "purpose": "Groups normalized statement line items by company, statement type, and fiscal period.",
        "grain": "One provider statement identity and period; provider-specific logical revisions may coexist.",
        "columns": {"Id": "Statement UUID", "CompanyId": "Nullable FK to Companies.Id", "ExternalCompanyId": "Provider company key", "ProviderName": "Source/provider", "ExternalStatementId": "Provider statement key", "StatementType": "BalanceSheet, IncomeStatement, or CashFlow", "PeriodType/PeriodStart/PeriodEnd": "Period duration and Gregorian bounds", "IsAudited": "false=Unaudited, true=Audited; business-owner-confirmed", "IsRepresented": "false=Not restated, true=Restated; business-owner-confirmed", "IsComposing": "false=Parent-company/separate, true=consolidated; business-owner-confirmed", "PublishedAt/VendorPeriodDate": "Disclosure and vendor period dates", "WarningsJson/SourcePayloadChecksum": "Quality and payload provenance"},
        "units": "Header contains period flags/dates; value units are on source-item definitions/metric registry, not the header.",
        "relationships": ["CompanyId -> Companies.Id (physical FK)", "FinancialStatementLineItems.FinancialStatementId is the application association; live schema does not enforce this FK."],
        "metricReferences": ["Feature 015 financial metric definitions and unit governance are authoritative."],
        "providerPolicy": "No global provider precedence rule is implemented. NoavaranCurrentApi fetches BalanceSheet, IncomeStatement, and CashFlow endpoint families; CyclicalWaves normalizes quarterly income statement fields only; CodalDb selects one variant per (PeriodEnd.Date, PeriodType), ordered IsAudited=true first, then IsRepresented=true (Restated) first, then IsComposing=true by default (or false when preferConsolidated=false), then StmtId ascending. Provider is distinct from selected statement revision; explicitly select provider and preserve provenance.",
        "periodSemantics": "PeriodType is duration (e.g. ThreeMonths), distinct from StatementType. IsRepresented=true denotes Restated per the business owner; the physical column is unchanged and there is no separate IsRestated column. Provider revision identity remains separate from this semantic flag.",
        "examples": ["فروش و سود خالص فصل اخیر از صورت سود و زیان", "Show audited income statement values for a fiscal period"],
        "businessDefinitions": [
            {"field": "IsAudited", "persian": "حسابرسی‌شده", "english": "Audited Financial Statement", "trueMeans": "Audited", "falseMeans": "Unaudited", "provenance": "business-owner-confirmed", "status": "confirmed"},
            {"field": "IsRepresented", "persian": "تجدید ارائه‌شده", "english": "Restated Financial Statement", "trueMeans": "Restated", "falseMeans": "Not restated", "provenance": "business-owner-confirmed", "status": "confirmed"},
            {"field": "IsComposing", "persian": "تلفیقی", "english": "Consolidated Financial Statement", "trueMeans": "Consolidated financial statement", "falseMeans": "Parent-company / separate financial statement", "provenance": "business-owner-confirmed", "status": "confirmed"}
        ],
        "cautions": ["IsComposing is independent of audit and restatement flags.", "CodalDb currently prefers IsComposing=true by default, which conflicts with Feature 141's generic-path default; the provider-specific behavior is unchanged.", "CodalDb restatement preference and StmtId tie-break have no additional business approval in this phase.", "CyclicalWaves period dates can be estimated and carry stale-data warnings.", "Do not infer latest accepted revision by MAX(PeriodEnd) alone."],
        "sourceVerification": "Live PostgreSQL schema; Noavaran client; CyclicalWaves and CodalDb normalizers; CodalDb statement-selection policy.",
    },
    "FinancialStatementSourceItems": {
        "descriptionEn": "Provider-specific source-item catalog defining statement line identities, labels, and source units.",
        "descriptionFa": "فرهنگ اقلام مبدأ صورت مالی شامل شناسه ارائه‌دهنده، عنوان و واحد مبدأ.",
        "purpose": "Provides canonical catalog IDs for line items and mappings to governed metric codes.",
        "grain": "One provider + statement type + provider SourceItemId definition.",
        "columns": {"Id": "Internal catalog UUID", "SourceItemId": "Provider-defined source item integer; not the row UUID", "ProviderName": "Provider namespace", "StatementType": "Applicable statement family", "TitleFa/TitleEn": "Localized labels", "Unit": "Source unit declaration"},
        "units": "Unit is provider/source definition metadata; normalize monetary and per-share units per Feature 015.",
        "relationships": ["FinancialStatementLineItems.SourceItemCatalogId -> FinancialStatementSourceItems.Id (physical FK)", "FinancialStatementSourceItemMetricMappings.SourceItemCatalogId -> FinancialStatementSourceItems.Id (physical FK)"],
        "metricReferences": ["FinancialStatementSourceItemMetricMappings.MetricCode -> Feature 015 metric registry identity."],
        "providerPolicy": "Never join by SourceItemId alone across providers; join by catalog UUID or provider + source key.",
        "periodSemantics": "Definition rows are not period observations.",
        "examples": ["کدام قلم مبدأ متناظر با درآمد عملیاتی است؟", "Find the provider source item mapped to net profit"],
        "cautions": ["One provider’s title or source ID does not imply equivalent meaning at another provider."],
        "sourceVerification": "EF Core mappings, live PostgreSQL FK inventory, and normalizer/metric mapping code.",
    },
    "FinancialStatementLineItems": {
        "descriptionEn": "Normalized statement values keyed to a metric code, statement row, and optional provider source-item definition.",
        "descriptionFa": "مقادیر نرمال‌شده اقلام صورت مالی با کد معیار و پیوند اختیاری به قلم مبدأ.",
        "purpose": "Stores statement-level metric observations before governed derived-metric recalculation.",
        "grain": "One FinancialStatementId + MetricCode observation in application logic.",
        "columns": {"FinancialStatementId": "Application link to statement header; no live FK found", "MetricCode": "Metric identity, including provider codes where applicable", "Value": "Numeric source/normalized observation", "SourceItemCatalogId": "Optional FK to source-item catalog"},
        "units": "Values can be million IRR for monetary statement items, IRR per share for EPS-like values, ratios, or percent; use source catalog Unit plus Feature 015 registry. Do not assume all values share a scale.",
        "relationships": ["SourceItemCatalogId -> FinancialStatementSourceItems.Id (physical FK)", "FinancialStatementId -> FinancialStatements.Id is expected application key but not a live FK."],
        "metricReferences": ["MetricCode and unit governance come from Feature 015; avoid duplicating formulas."],
        "providerPolicy": "Provider-specific normalization and selection only; do not coalesce duplicate provider values without the existing selection rules.",
        "periodSemantics": "Inherits statement header period.",
        "examples": ["درآمد صورت مالی فصل اخیر", "Show net profit from a selected statement period"],
        "cautions": ["Live inventory reports a uniqueness mismatch for (FinancialStatementId, MetricCode): EF expects unique, while PostgreSQL index is non-unique.", "Per-share IRR must remain distinct from million-IRR statement values."],
        "sourceVerification": "Live PostgreSQL/EF reconciliation, Feature 015 registry, provider normalizers.",
    },
    "FinancialStatementSourceItemMetricMappings": {
        "descriptionEn": "Mapping from one provider source-item definition to a governed MetricCode.",
        "descriptionFa": "نگاشت قلم مبدأ ارائه‌دهنده به کد معیار حاکم.",
        "purpose": "Connects provider-specific line items to the normalized financial metric registry.",
        "grain": "One source-item catalog UUID to one metric mapping; EF declares SourceItemCatalogId unique.",
        "columns": {"Id": "Mapping UUID", "SourceItemCatalogId": "FK to FinancialStatementSourceItems.Id", "MetricCode": "Governed normalized metric identity"},
        "units": "Units are inherited from source item and governed metric definition.",
        "relationships": ["SourceItemCatalogId -> FinancialStatementSourceItems.Id (physical FK)"],
        "metricReferences": ["MetricCode -> Feature 015 financial metric catalog."],
        "providerPolicy": "Source item catalog identity is provider-specific; mapping is not evidence of provider precedence.",
        "periodSemantics": "Static mapping, not a dated observation.",
        "examples": ["Map provider line items to normalized financial metrics"],
        "cautions": ["Do not infer a one-to-one meaning across providers from matching MetricCode alone."],
        "sourceVerification": "EF configuration, live PostgreSQL FK, and metric registry.",
    },
    "DerivedMetrics": {
        "descriptionEn": "Versioned per-company, per-metric, per-period values with calculation policy and source/dependency evidence.",
        "descriptionFa": "مقادیر معیار نسخه‌دار برای شرکت و دوره، همراه با سیاست محاسبه و شواهد منبع و وابستگی.",
        "purpose": "Persisted output of the governed calculation pipeline and selected externally supplied mapped values.",
        "grain": "ExternalCompanyId + MetricCode + MetricVersion + CalculationPolicyVersion + PeriodEnd (application upsert key).",
        "columns": {"ExternalCompanyId": "Provider-facing company identity; no Companies FK", "MetricCode": "Governed metric identity", "MetricVersion/CalculationPolicyVersion": "Semantic and formula versions", "PeriodType/PeriodStart/PeriodEnd": "Observation period", "Value/Unit": "Numeric observation and unit key", "SourceEvidenceJson/DependencyEvidenceJson": "Input provenance and calculation dependencies", "ObservedAt/LastSynchronizedAt": "Observation and ingestion timestamps"},
        "units": "Use Unit and Feature 015 metric definition; values include unit-dependent monetary, per-share, ratio, and percentage measures.",
        "relationships": ["No physical FK to Companies; resolve ExternalCompanyId only with matching provider context/source evidence."],
        "metricReferences": ["Feature 015 is canonical for metric identity, units, formulas, and dependencies."],
        "providerPolicy": "CyclicalWaves statement/monthly data is normalized first and then recalculated through the provider-agnostic metric worker; it does not directly write DerivedMetrics in its normalizers. Noavaran fundamental-index mapped/vendor-precomputed values are separately normalized into DerivedMetrics with VendorPrecomputed evidence. Preserve source provenance; no provider precedence is implied.",
        "periodSemantics": "Versioned metric observation period, not necessarily a latest-market snapshot.",
        "examples": ["مقادیر معیارهای مالی با منبع و دوره مشخص", "Show a governed metric with its calculation version and evidence"],
        "cautions": ["ExternalCompanyId alone is not globally unique.", "Do not recompute or reinterpret values without the registered metric/policy version."],
        "sourceVerification": "DerivedMetricResultStore, MetricRecalculationProcessor, NoavaranFundamentalIndexNormalizer, and CyclicalWaves persistence audit.",
    },
    "CompanyIndustryRelativeValuations": {
        "descriptionEn": "Company rows from a versioned industry-relative valuation calculation, including PE, PS, equilibrium, validity, classification, and rank results.",
        "descriptionFa": "نتایج ارزش‌گذاری نسبی شرکت در صنعت شامل P/E، P/S، قیمت تعادلی، اعتبار، طبقه‌بندی و رتبه نسخه‌دار.",
        "purpose": "Persist a governed valuation calculation snapshot and its source-observation provenance.",
        "grain": "One company per IndustryRelativeValuationCalculations.CalculationId; ranking is scoped to that calculation/version.",
        "columns": {"CompanyId": "FK to Companies.Id", "CalculationId": "FK to calculation header", "CurrentPE/CurrentPS/HistoricalAveragePE/HistoricalAveragePS": "Inputs and comparison values", "PEPercent/PSPercent/EquilibriumPercent": "Relative deviations", "PEClassification/PSClassification/EquilibriumClassification": "Classification outputs", "IsValid/IsOutlier/Reason": "Quality flags and explanations", "GlobalRank/RankVersion": "Persisted rank and ranking version", "SourceFactId/ObservationId/Timestamp/Version/Watermark": "Source lineage per result family"},
        "units": "PE/PS are ratios; prices use the registered market-price unit; percentages are percent values as defined by calculator.",
        "relationships": ["CompanyId -> Companies.Id (physical FK)", "CalculationId -> IndustryRelativeValuationCalculations.Id (physical FK)"],
        "metricReferences": ["Feature 125 engine IQR-R7-1.5-v1 normalizes positive current/reference pairs as current ÷ reference × 100; benchmarks per group/metric with R-7 quartiles and 1.5×IQR bounds; requires at least two clean observations; classifies at/below clean average Green and above it Red; ranks by Green count, then PE/PS/equilibrium percentages, valid count, and CompanyId. Not a generic table formula."],
        "providerPolicy": "Consume complete calculation snapshot and its source provenance; canonical provider membership and source fact identity/freshness are enforced by Feature 125 calculation orchestration. A valid IQR outlier is excluded from the benchmark and is Unclassifiable; do not recompute GlobalRank.",
        "periodSemantics": "Calculation snapshot timestamp/source observation watermarks; not a fiscal statement period.",
        "examples": ["رتبه نسبی P/E شرکت در صنعت", "Show the persisted industry-relative valuation rank"],
        "cautions": ["GlobalRank is versioned calculation output and must not be recomputed by sorting raw fields.", "Invalid/outlier values and calculation freshness must be preserved."],
        "sourceVerification": "EF/live FKs and Feature 125 IndustryRelativeValuationEngine IQR-R7-1.5-v1, calculation input builder, snapshot writer, and orchestration.",
    },
    "CompanyMonthlyActivityTrendSnapshots": {
        "descriptionEn": "Persisted company-month activity trend projection with governed comparisons, completeness, output types, and source provenance.",
        "descriptionFa": "نمای روند فعالیت ماهانه شرکت با مقایسه‌های حاکم، کامل‌بودن داده، نوع خروجی و منشأ منبع.",
        "purpose": "Chart-ready monthly production/sales trend responses; the AI path reads snapshots instead of aggregating raw line items at request time.",
        "grain": "Company external identity + report year/month and governed output-type selections.",
        "columns": {"ExternalCompanyId": "Provider-facing company key", "ReportYear/ReportMonth": "Reported month", "MonthlySalesAmount/MonthlyProductionQuantity/MonthlySalesQuantity": "Persisted monthly totals", "MonthlyAverageSalesRate": "Governed average rate", "SameMonthPreviousYear*": "Prior-year comparison measures", "Average12Month*": "Rolling average and period count", "Ytd*": "Year-to-date values", "*GrowthPercent": "Governed period comparisons", "CurrentMonthOutputType/YtdOutputType/YtdPreviousMonthOutputType": "Source output-type policies", "HasMixedProductUnits/ProductUnitSummary/DataCompletenessScore": "Quality/comparability state", "SourceProviderName/SourceReportId/SourceRawPayloadId": "Provenance", "CalculatedAtUtc": "Snapshot calculation timestamp"},
        "units": "Persisted SalesAmount is million IRR; product quantities retain provider units; response layer converts amount to billion toman (value × 0.0001) where that contract applies. Mixed units are flagged.",
        "relationships": ["ExternalCompanyId has no physical FK to Companies; resolver joins must use provider identity policy."],
        "metricReferences": ["Feature 076/077/136 trend snapshot and response policy is authoritative; do not create raw-line generic trend calculations."],
        "providerPolicy": "Respect SourceProviderName, output-type fields, data completeness, and existing specialized response policy.",
        "periodSemantics": "ReportYear/ReportMonth are report month; calendar/fiscal fields are separate. CalculatedAtUtc is not report period end.",
        "examples": ["روند فروش ماهانه شرکت X", "Compare monthly sales with the same month last year"],
        "cautions": ["Mixed product units make aggregate quantities incomparable.", "Persisted trend data may be stale or incomplete; do not equate latest date with complete coverage."],
        "sourceVerification": "Trend snapshot contracts/calculator/query path and README monetary conversion policy.",
    },
    "IntradayTradeSnapshots": {
        "descriptionEn": "Intraday instrument trade/quote observations from normalized market-data provider feeds.",
        "descriptionFa": "مشاهدات درون‌روزی معامله و قیمت برای ابزار معاملاتی از منابع نرمال‌شده داده بازار.",
        "purpose": "Append-oriented market snapshot history for instrument-level intraday reads.",
        "grain": "One provider ExternalSnapshotId for one TradingInstrument at TradingDate + TradingTime; ReceivedAt records ingestion receipt.",
        "columns": {"TradingInstrumentId": "FK to TradingInstruments.Id, not Companies.Id", "TradingDate/TradingTime": "Exchange-local observation date/time fields (time has no time zone)", "LastTradedPrice/ClosingPrice/PriceChange/PriceYesterday": "Price fields", "Volume/TotalTransactions/TotalCapital": "Market activity fields", "ProviderName/ExternalSnapshotId": "Source identity", "ReceivedAt": "Ingestion receipt timestamp"},
        "units": "Prices and market activity are numeric source values copied by the adapters; PostgreSQL stores no unit/scale metadata and no universal conversion is applied. Verify ProviderName and source mapping before presentation; do not combine provider scales by column name alone.",
        "relationships": ["TradingInstrumentId -> TradingInstruments.Id (physical FK)", "TradingInstruments.NormalizedCompanyId -> Companies.Id is optional; no direct company FK."],
        "metricReferences": ["Existing market quote projection owns latest-price semantics."],
        "providerPolicy": "ProviderName and ExternalSnapshotId identify each source row. The direct TSETMC adapter persists TSE TradeLastDay observations; StockMarketDB is a separate ingestion path. Repository worker defaults enable TSETMC polling and disable StockMarketDB polling, but deployment overrides/current runtime state are not certified here.",
        "periodSemantics": "Intraday point observations; trading date/time are not UTC timestamps. ReceivedAt is a separate timestamp with time zone.",
        "examples": ["آخرین قیمت ثبت‌شده درون‌روزی نماد", "Show intraday price snapshots for an instrument"],
        "cautions": ["Polling gaps, retention, and market calendar affect completeness.", "MAX(TradingDate/TradingTime) does not prove a complete/current market feed."],
        "sourceVerification": "TsetmcDirectFeedSyncService and StockMarketDbSyncService mappings, worker defaults, live PostgreSQL FK/type metadata.",
    },
    "DailyInstrumentTrades": {
        "descriptionEn": "One normalized daily instrument trade/price observation per source trading date and instrument.",
        "descriptionFa": "مشاهده روزانه قیمت و معامله برای هر ابزار در تاریخ معاملاتی منبع.",
        "purpose": "Daily historical observations distinct from intraday snapshots.",
        "grain": "TradingInstrument + TradingDate under provider daily-trade identity policy.",
        "columns": {"TradingInstrumentId": "FK to TradingInstruments.Id", "TradingDate": "Source trading date", "ClosingPrice/LastTradedPrice/PriceChange/PriceYesterday": "Daily price fields", "Volume/MarketValue/TotalCapital/TotalTransactions": "Daily activity fields", "ExternalTradeId": "Source trade-row GUID", "SourceInsertedAt": "Source insertion timestamp"},
        "units": "Price, MarketValue, and volume are numeric source values copied by the adapters; PostgreSQL stores no unit/scale metadata and no universal conversion is applied. Verify ProviderName and source mapping before presentation; do not combine provider scales by column name alone.",
        "relationships": ["TradingInstrumentId -> TradingInstruments.Id (physical FK); optional instrument-to-company relationship through NormalizedCompanyId."],
        "metricReferences": ["Daily observation; not a latest quote projection or an intraday event."],
        "providerPolicy": "ProviderName and ExternalTradeId identify the normalized row. The direct TSETMC adapter uses daily trade history; StockMarketDB has a distinct TradeRefined path. Retain source provenance and avoid cross-provider deduplication by date/instrument alone.", 
        "periodSemantics": "Daily trading-calendar date, not necessarily Gregorian calendar day coverage.",
        "examples": ["قیمت پایانی روز معاملاتی نماد", "Show daily close and volume for a trading date"],
        "cautions": ["Non-trading days have no row; check source completeness/watermark.", "Do not infer currentness from max date alone."],
        "sourceVerification": "TsetmcDirectFeedSyncService and StockMarketDbSyncService mappings; live PostgreSQL FK/type metadata.",
    },
    "IntradayIndexSnapshots": {
        "descriptionEn": "Intraday observations for market/index instruments.", "descriptionFa": "مشاهدات درون‌روزی برای ابزارهای شاخص بازار.",
        "purpose": "Intraday index series; the instrument table defines index identity.",
        "grain": "Provider ExternalSnapshotId per TradingInstrumentId and TradingDate/TradingTime.",
        "columns": {"TradingInstrumentId": "FK to TradingInstruments.Id", "TradingDate/TradingTime": "Exchange-local date and timezone-free time", "Value/ChangePercent": "Index level and change measure", "ProviderName/ExternalSnapshotId": "Provider snapshot identity", "SourceChangedAt": "Source change timestamp"},
        "units": "Index level and percentage change follow source mapping; numeric columns do not encode unit.",
        "relationships": ["TradingInstrumentId -> TradingInstruments.Id (physical FK); no direct Companies relationship."],
        "metricReferences": [], "providerPolicy": "ProviderName and ExternalSnapshotId identify each observation. Direct TSETMC and StockMarketDB index adapters are separate sources; repository worker defaults enable TSETMC polling and disable StockMarketDB polling, subject to deployment overrides.",
        "periodSemantics": "Multiple observations within exchange trading date; SourceChangedAt is a separate source timestamp.",
        "examples": ["روند درون‌روزی شاخص کل", "Show intraday index levels for the trading session"],
        "cautions": ["Snapshot polling is not guaranteed complete; max observation is not completeness proof.", "Index identity is on TradingInstruments, not company identity."],
        "sourceVerification": "TsetmcDirectFeedSyncService and StockMarketDbSyncService mappings; live PostgreSQL FK/type metadata.",
    },
    "DailyIndexSnapshots": {
        "descriptionEn": "Daily index observations keyed by instrument and trading date.", "descriptionFa": "مشاهدات روزانه شاخص با کلید ابزار و تاریخ معاملاتی.",
        "purpose": "Daily historical index levels and close-derived observations.",
        "grain": "One TradingInstrumentId + TradingDate observation per provider policy.",
        "columns": {"TradingInstrumentId": "FK to TradingInstruments.Id", "TradingDate": "Trading-calendar date", "Value/High/Low/ChangePercent": "Daily level/range/change fields", "ProviderName/SourceKind": "Provider and derivation/source family", "ObservedAt": "Observation timestamp"},
        "units": "Index levels and percent change per source adapter; not a monetary company metric.",
        "relationships": ["TradingInstrumentId -> TradingInstruments.Id (physical FK)."],
        "metricReferences": [], "providerPolicy": "ProviderName and SourceKind distinguish source history and derived rows. Direct TSETMC and StockMarketDB daily-index adapters are separate sources; retain SourceKind and source lineage, and do not merge by instrument/date alone.",
        "periodSemantics": "One daily observation per named index and trading date; trading date coverage follows exchange calendar.",
        "examples": ["مقدار پایانی روزانه شاخص کل", "Show daily index history for a trading period"],
        "cautions": ["Historical source tables can be stale; use source-specific currentness policy.", "Do not treat max TradingDate as complete/current without watermark and source semantics."],
        "sourceVerification": "TsetmcDirectFeedSyncService and StockMarketDbSyncService mappings; live PostgreSQL FK/type metadata.",
    },
}


REPORTING_TYPES = [
    {"code": 1000000, "persian": "تولیدی", "english": "Manufacturing", "route": "ProductSales v2", "verified": True},
    {"code": 1000001, "persian": "ساختمانی", "english": "Construction", "route": "Unsupported: no verified monthly endpoint", "verified": True},
    {"code": 1000002, "persian": "سرمایه‌گذاری", "english": "Investment", "route": "Unsupported: no verified monthly endpoint", "verified": True},
    {"code": 1000003, "persian": "بانکی", "english": "Banking", "route": "Unsupported: no verified monthly endpoint", "verified": True},
    {"code": 1000004, "persian": "لیزینگی", "english": "Leasing", "route": "Unsupported: no verified monthly endpoint", "verified": True},
    {"code": 1000005, "persian": "خدماتی", "english": "Services", "route": "ServiceSales v3", "verified": True},
    {"code": 1000006, "persian": "بیمه‌ای", "english": "Insurance", "route": "Unsupported: no verified monthly endpoint", "verified": True},
    {"code": 1000007, "persian": "حمل و نقل دریایی", "english": "Maritime Transportation", "route": "Unsupported: no verified monthly endpoint", "verified": True},
    {"code": 1000008, "persian": "کشاورزی", "english": "Agriculture", "route": "ProductSales v2", "verified": True},
    {"code": 1000009, "persian": "تأمین سرمایه", "english": "Investment Banking", "route": "Unsupported: no verified monthly endpoint", "verified": True},
]


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main():
    snapshot = read_json(SNAPSHOT)
    physical = {f"public.{o['Name']}": o for o in snapshot["PostgreSqlObjects"] if o["Schema"] == "public"}
    candidates = []
    for name, semantic in CURATIONS.items():
        table_id = "tbl_" + __import__("hashlib").sha256(f"public.{name}".encode()).hexdigest()[:16]
        path = TABLES / f"{table_id}.yaml"
        record = read_json(path)
        if record["physical"]["Name"] != name:
            raise SystemExit(f"Stable table id mismatch for {name}")
        status = record.get("semantic", {}).get("status")
        if status == "approved" or record.get("semantic", {}).get("queryable"):
            raise SystemExit(f"Refusing to overwrite approved/queryable metadata: {name}")
        record.setdefault("semantic", {}).update(semantic)
        record["semantic"].update({"status": "candidate-pending-review", "queryable": False, "approvalStatus": "human-review-required"})
        record["semantic"]["semanticApproval"] = {"state": "candidate-pending-review", "approved": False, "provenance": "existing catalog review state; no reviewer approval fabricated"}
        record["semantic"]["queryAuthorization"] = {"state": "not-approved", "runtimeQueryable": False, "approvedObjects": [], "reason": "Semantic review and navigation inclusion do not grant query access."}
        if name == "FinancialStatements":
            record["semantic"]["statementScopePolicy"] = {"provenance": "business-owner-confirmed", "status": "confirmed-for-feature-141-generic-path", "defaultFilter": {"field": "IsComposing", "value": False}, "explicitConsolidatedValue": True, "compareScopes": "Retrieve separately; never merge.", "fallback": "No silent substitution when parent-company data is unavailable.", "independentDimensions": ["IsAudited", "IsRepresented", "IsComposing"], "providerBehaviorChange": False}
        write_json(path, record)
        if f"public.{name}" not in physical:
            raise SystemExit(f"Selected object missing from live PostgreSQL snapshot: {name}")
        physical_columns = {c["Name"] for c in physical[f"public.{name}"]["Columns"]}
        catalog_columns = {c["Name"] for c in record["physical"]["Columns"]}
        if physical_columns != catalog_columns:
            raise SystemExit(f"Catalog/snapshot column drift for {name}")
        candidates.append({
            "table": f"public.{name}", "id": table_id,
            "purpose": semantic["purpose"], "classification": "public financial reference or observation data",
            "rowOwnership": semantic["grain"], "sourcePolicy": semantic["providerPolicy"],
            "owner": "Financial data domain", "queryable": False,
            "semanticApproval": {"state": "candidate-pending-review", "approved": False},
            "queryAuthorization": {"state": "not-approved", "runtimeQueryable": False},
            "status": "candidate-pending-review", "evidence": semantic["sourceVerification"],
        })

    scope = read_json(SCOPE)
    scope["generatedFrom"] = "live PostgreSQL metadata, EF Core reconciliation, application source, and owner-supplied definitions"
    scope["liveVerified"] = True
    scope["includedCandidates"] = candidates
    scope["businessDefinitions"] = {
        "reportingTypeMappingSource": "Business-owner supplied definitions; labels are not inferred from integer codes.",
        "reportingTypes": REPORTING_TYPES,
        "salesAmountUnit": {"value": "million IRR", "source": "Business-owner supplied definition corroborated by persisted monthly trend contract", "status": "verified-with-owner-supplied-definition"},
        "salesRate": {"expression": "SalesAmount / SalesQuantity", "unit": "million IRR per source quantity unit", "policy": "Do not recalculate if quantity is null/zero or when the provider rate is authoritative; preserve negative source values and report quality separately."},
        "financialStatementFlags": [
            {"field": "IsAudited", "persian": "حسابرسی‌شده", "english": "Audited Financial Statement", "trueMeans": "Audited", "falseMeans": "Unaudited", "provenance": "business-owner-confirmed", "status": "confirmed"},
            {"field": "IsRepresented", "persian": "تجدید ارائه‌شده", "english": "Restated Financial Statement", "trueMeans": "Restated", "falseMeans": "Not restated", "provenance": "business-owner-confirmed", "status": "confirmed"},
            {"field": "IsComposing", "persian": "تلفیقی", "english": "Consolidated Financial Statement", "trueMeans": "Consolidated financial statement", "falseMeans": "Parent-company / separate financial statement", "provenance": "business-owner-confirmed", "status": "confirmed"}
        ],
    }
    scope["querySelectionPolicy"] = {
        "id": "financial-statement-scope-default-v1",
        "scope": "Feature 141 generic semantic query path only",
        "default": "IsComposing=false (parent-company / separate financial statement)",
        "explicitConsolidated": "Use IsComposing=true only when consolidated statements are requested.",
        "comparison": "Retrieve separate and consolidated values as separately scoped results; never merge them.",
        "missingDefaultScope": "Never silently substitute consolidated data when parent-company data is unavailable; return a no-data or unsupported outcome.",
        "independentDimensions": ["IsAudited", "IsRepresented", "IsComposing"],
        "noUnapprovedDefaults": ["IsAudited", "IsRepresented"],
        "legacyBehavior": "Do not change CodalDb or other provider-specific selection behavior in this phase.",
        "provenance": "business-owner-confirmed", "status": "confirmed-for-feature-141-generic-path"
    }
    scope["dataSourceLineage"] = {
        "Companies": {"path": ["TSETMC", "Novaran Amin", "Novaran API", "Financial Copilot Companies"], "provenance": "business-owner-confirmed", "sourceCode": "Noavaran API company normalizer persists provider-scoped company identity and attributes."},
        "TradingInstruments": {"path": ["TSETMC", "direct TSETMC ingestion / historical migration", "Financial Copilot TradingInstruments"], "provenance": "business-owner-confirmed historical lineage", "currentMechanism": "TsetmcDirectFeedSyncService persists TradingInstruments; TsetmcPollingWorker schedules end-of-day instrument synchronization when enabled and the adapter is operational. Repository Worker defaults enable TSETMC polling and disable StockMarketDB polling; deployment effective settings are not verified.", "stockMarketDbInstrumentBehavior": "StockMarketDbSyncService fetches/counts instrument records for watermarking but deliberately does not write TradingInstruments."},
        "identityBoundary": "Companies.Id is the internal canonical company UUID; Companies.ProviderName + ExternalCompanyId is a provider-scoped company identity. TradingInstruments.Id is a separate internal instrument UUID; ProviderName + ExternalInstrumentId is its source namespace and adapter-generated external identity; InstrumentCode maps TSETMC InsCode; NormalizedCompanyId is the only declared instrument-to-company FK. Never equate these IDs or join by ticker/name alone."
    }
    scope["monthlyReportProviderRoutes"] = {
        "source": "NoavaranMonthlyReportTypeResolver and NadpcoApiDataProviderClient",
        "minimumShamsiYear": 1404,
        "routes": [
            {"reportingType": 1000000, "companyType": "Manufacturing", "service": "api/v2/MonthlyActivity/ProductSales", "outputTypes": [0, 1, 2, 3, 4], "monthlyActualPolicy": "OutputType=0"},
            {"reportingType": 1000005, "companyType": "Services", "service": "api/v3/MonthlyActivity/ServiceSales", "outputTypes": [None]},
            {"reportingType": 1000008, "companyType": "Agriculture", "service": "api/v2/MonthlyActivity/ProductSales", "outputTypes": [0, 1, 2, 3, 4], "monthlyActualPolicy": "OutputType=0"},
        ],
        "unsupportedReportingTypes": [1000001, 1000002, 1000003, 1000004, 1000006, 1000007, 1000009],
        "coverageCaveat": "A configured route proves code support, not that every company/month has returned and persisted rows. ProductSales fetches output types independently; service does not probe product endpoint. A failed type-0 fetch is not reclassified as a service company.",
    }
    scope["financialStatementProviderPolicy"] = {
        "precedence": "No global provider precedence is implemented or asserted.",
        "NoavaranCurrentApi": "Fetches BalanceSheet, IncomeStatement, and CashFlow values through separate api/v2/FS/*/Values endpoint families; statement selection is parameterized by configured year/period/audited/represented/composing filters.",
        "CyclicalWaves": "Persian ticker detail normalizes quarterly IncomeStatement only (Q0/Q1/Q4 relative periods) with stale-date warnings; it supplies selected PE/PS and average values as precomputed source line items, then Feature 015 recalculation persists derived metrics.",
        "CodalDb": "Codal source-item normalizers plus CodalDbStatementSelectionPolicy govern source-row normalization and statement selection. Within each (PeriodEnd.Date, PeriodType) group it orders IsAudited=true first, then IsRepresented=true, then IsComposing=true by default (preferConsolidated=true), then StmtId ascending; this is not a global provider precedence rule. Do not merge provider rows without selecting provider.",
        "restatement": "IsRepresented=true means Restated and false means Not restated, provenance business-owner-confirmed; the physical column remains IsRepresented and provider/revision identity is separate.",
        "consolidation": "IsComposing=true means consolidated and false means parent-company/separate, provenance business-owner-confirmed. This generic-path definition does not change provider-specific selection behavior.",
    }
    scope["codalSelectionPolicyReview"] = {
        "implemented": "Within each (PeriodEnd.Date, PeriodType) group: IsAudited=true first; IsRepresented=true second; IsComposing=true by default (preferConsolidated=true); then StmtId ascending. When preferConsolidated=false only the IsComposing ordering reverses; audited and represented ordering is unchanged.",
        "semanticInterpretation": "IsRepresented=true is Restated per the product owner.",
        "businessApproval": "The product owner confirmed the flag meaning, not the policy to always prefer restated variants. This preference is implemented but not independently approved.",
        "risk": "Audited status outranks restatement status, so an audited non-restated row can beat an unaudited restated row. Among ties on earlier flags, StmtId ascending is deterministic but is not a newest-announcement rule and can select an older source identity. Grouping only by date and period type also depends on provider query scope to avoid mixing statement families.",
        "decisionRequired": "The preference for restated variants and StmtId tie-break still require business review before generic reads rely on them. The generic Feature 141 default is IsComposing=false, which conflicts with current CodalDb preferConsolidated=true behavior. No code sorting was changed.",
        "genericPathConflict": "Feature 141 defaults to IsComposing=false; CodalDb currently prefers IsComposing=true by default. This phase records the conflict only.",
        "status": "documented-conflict-no-runtime-change"
    }
    scope["industryRelativeValuationPolicy"] = {
        "algorithmVersion": "IQR-R7-1.5-v1",
        "normalization": "CurrentValue / ReferenceValue * 100; only positive current and reference values with valid identity, available and fresh source facts are valid.",
        "benchmark": "Per canonical group and metric; R-7 Q1/Q3; Tukey 1.5*IQR bounds; outliers excluded; clean average available only with at least two clean observations.",
        "classification": "Valid non-outlier percent <= clean average is Green; above is Red. Outliers and unavailable benchmark cases are Unclassifiable; invalid non-positive input is Red.",
        "ranking": "Per group: Green metric count descending; then PE, PS, Equilibrium percent ascending with missing values last; valid metric count descending; CompanyId ascending.",
        "evidence": "IndustryRelativeValuationEngine, calculation input builder, and snapshot writer; snapshots retain algorithm version, membership hash, source barrier hash, and source observations.",
    }
    scope["relationshipsAndGaps"] = [
        "Physical FK: Companies -> Markets, Industries, IndustryGroups; Industries.ParentId -> Industries.Id.",
        "Physical FK: MonthlyReports.CompanyId -> Companies.Id; ExternalCompanyId is a provider-scoped text key. MonthlyReportLineItems.MonthlyReportId -> MonthlyReports.Id is not enforced by a live FK.",
        "Physical FK: FinancialStatements.CompanyId -> Companies.Id; FinancialStatementLineItems.FinancialStatementId -> FinancialStatements.Id is not enforced by a live FK in the captured PostgreSQL schema; SourceItemCatalogId does have an FK.",
        "Physical FK: SourceItemMetricMappings.SourceItemCatalogId -> FinancialStatementSourceItems.Id.",
        "Physical FK: CompanyIndustryRelativeValuations.CompanyId -> Companies.Id and CalculationId -> IndustryRelativeValuationCalculations.Id.",
        "Physical FK verified in PostgreSQL and EF: IntradayTradeSnapshots.TradingInstrumentId -> TradingInstruments.Id; required, ON DELETE CASCADE.",
        "Physical FK verified in PostgreSQL and EF: DailyInstrumentTrades.TradingInstrumentId -> TradingInstruments.Id; required, ON DELETE CASCADE.",
        "Physical FK verified in PostgreSQL and EF: IntradayIndexSnapshots.TradingInstrumentId -> TradingInstruments.Id; required, ON DELETE CASCADE.",
        "Physical FK verified in PostgreSQL and EF: DailyIndexSnapshots.TradingInstrumentId -> TradingInstruments.Id; required, ON DELETE CASCADE.",
        "Physical FK verified in PostgreSQL and EF: TradingInstruments.NormalizedCompanyId -> Companies.Id; optional, non-unique, ON DELETE SET NULL.",
        "TSETMC application mapping matches source InsCode text to Companies.InstrumentCode, then selects latest LastSynchronizedAt and Id; it does not filter Company.ProviderName and Companies.InstrumentCode is not unique. Treat the resulting link as potentially ambiguous/cross-provider; missing links remain null.",
        "DerivedMetrics and CompanyMonthlyActivityTrendSnapshots use ExternalCompanyId without a Companies FK; do not join globally without provider identity.",
        "FinancialStatementLineItems(FinancialStatementId, MetricCode) differs between EF uniqueness and live PostgreSQL index; resolve drift before exposing that grain.",
    ]
    scope["additionalDataSources"] = [{
        "id": "src_nadpco_company_fundamental_index_values", "kind": "external-provider-endpoint",
        "semanticApproval": {"state": "candidate-pending-review", "approved": False},
        "queryAuthorization": {"state": "not-approved", "runtimeQueryable": False, "reason": "External endpoint is not a PostgreSQL table."},
        "provider": "NoavaranCurrentApi", "physicalIdentity": "POST api/v2/CompanyFundamentalIndex/Values",
        "sourceDataset": "FundamentalIndexes", "rawPersistence": "ProviderRawPayloads (FinancialProviderDbContext), keyed idempotently by ProviderName + Checksum; stores endpoint, ExternalReference, payload, checksum, ReceivedAt.",
        "normalizedPersistence": "NadpcoApiFundamentalIndexNormalizer -> DerivedMetrics for scale-verified mapped indexes; it selects canonical variants, prefers audited then non-represented then composing and latest announcement/source ID, and preserves VendorPrecomputed source evidence.",
        "notACompanyTable": True, "queryable": False, "status": "candidate-pending-review",
    }]
    scope["proposedApprovedReadScope"] = {
        "objects": [c["table"] for c in candidates],
        "externalSources": ["src_nadpco_company_fundamental_index_values"],
        "status": "proposal-pending-human-approval", "queryable": False,
        "preferredAccess": "Governed Feature 015 metric/read services and existing Feature 076/077/136 monthly-trend projections; do not expose raw tables where provider, accepted-revision, units, or calculation policy would be bypassed.",
        "excludedDomains": ["billing", "identity", "notifications", "operational sync state", "raw payload content"],
    }
    scope["exampleQuestionSupport"] = {
        "answerableThroughExistingGovernedPaths": ["company financial metrics already registered in Feature 015", "monthly activity trends already persisted by Feature 076/077/136", "instrument quote/daily history where the existing market read model has complete coverage"],
        "notApprovedForGenericSemanticSQL": ["cross-company product category comparisons without reviewed aliases", "raw monthly growth/rate recalculation", "unselected provider statement joins", "restatement comparisons until Codal selection policy and provider revision semantics receive human review", "complete/current market claims based on maximum timestamp", "direct raw payload queries", "company-level market joins inferred from ticker or company name"],
    }
    write_json(SCOPE, scope)

    source_path = CATALOG / "sources" / "src_nadpco_company_fundamental_index_values.yaml"
    source_path.parent.mkdir(parents=True, exist_ok=True)
    write_json(source_path, scope["additionalDataSources"][0])
    catalog = read_json(CATALOG / "catalog.yaml")
    catalog["sources"] = ["src_nadpco_company_fundamental_index_values"]
    write_json(CATALOG / "catalog.yaml", catalog)

    render_markdown(scope)


def render_markdown(scope):
    lines = [
        "# Feature 141 — Curated Financial Semantic Catalog Review", "",
        "Generated from `SemanticCatalog/catalog-scope.yaml`, selected table YAML metadata, and the external-source YAML record. This is a review proposal; no semantic entry is executable.", "",
        "## Approval and evidence boundary", "",
        "- Physical keys, column types, and declared foreign keys: live PostgreSQL `pg_catalog` snapshot, reconciled to EF Core.",
        "- Provider and calculation semantics: application source and existing feature policies cited per table.",
        "- ReportingType labels and the million-IRR business definition: owner-supplied definitions; labels are not inferred from codes.",
        "- Company and instrument historical lineage: product-owner-confirmed; current synchronization behavior: source-code verified; deployed settings are unverified.",
        "- IsAudited, IsRepresented, and IsComposing true/false meanings: `business-owner-confirmed`; the three flags are independent.",
        f"- Curated scope: {len(scope['includedCandidates'])} physical tables plus {len(scope['additionalDataSources'])} external provider endpoint; all physical entries remain `candidate-pending-review`; `queryable: false`.", "",
        "## Confirmed company and instrument lineage", "",
        "Companies: `TSETMC → Novaran Amin → Novaran API → Financial Copilot Companies`. TradingInstruments: `TSETMC → direct TSETMC ingestion / historical migration → Financial Copilot TradingInstruments`. These are distinct source paths and identities.",
        "Repository code registers TsetmcDirectFeedSyncService and TsetmcPollingWorker schedules end-of-day instrument sync when enabled and operational. Worker defaults enable TSETMC polling and disable StockMarketDB polling; deployment overrides/current worker state were not verified. StockMarketDbSyncService fetches instrument rows for watermarking but explicitly does not write TradingInstruments.",
        "`Companies.Id` is the canonical company UUID; `ProviderName + ExternalCompanyId` is a provider-scoped company key. `TradingInstruments.Id` is the internal instrument UUID. TSETMC `InsCode` maps to `InstrumentCode`; `ExternalInstrumentId` is a deterministic GUID derived from InsCode; the ISIN-like source `InstrumentID` maps to `InstrumentIsin`. The optional `NormalizedCompanyId` FK is distinct from all of these. In current TSETMC persistence both `SourceChangedAt` and `LastSynchronizedAt` are set to adapter synchronization time, not a vendor change time.", "",
        "## ReportingType mapping and monthly-report ingestion", "",
        "| Code | Persian definition | English definition | Noavaran route |", "|---:|---|---|---|",
    ]
    for item in scope["businessDefinitions"]["reportingTypes"]:
        route = next((r["service"].replace("api/v2/MonthlyActivity/", "").replace("api/v3/MonthlyActivity/", "") for r in scope["monthlyReportProviderRoutes"]["routes"] if r["reportingType"] == item["code"]), "Unsupported")
        lines.append(f"| {item['code']} | {item['persian']} | {item['english']} | {route} |")
    lines += ["", "Manufacturing (`1000000`) and agriculture (`1000008`) use `ProductSales` v2. Services (`1000005`) use `ServiceSales` v3. The code has no verified endpoint for the remaining seven supplied categories. The current API monthly-activity boundary starts at Shamsi 1404. Endpoint support does not prove persisted coverage for every company or month.", "",
        "### Units and null policy", "",
        "`SalesAmount` is million IRR per the supplied business definition and corroborating trend persistence contract. A line-level `SalesRate` is source/provider supplied and implies million IRR per source quantity unit. The separate `MonthlyAverageSalesRate` snapshot is total monthly sales amount divided by total sales quantity for selected product rows when units are not mixed and total quantity is nonzero. Keep source `Unit`, preserve negative values, and do not substitute that snapshot calculation for line-level values.", "",
        "## Financial statement flag definitions", "",
        "| Physical field | Persian | English | `true` means | `false` means | Provenance/status |", "|---|---|---|---|---|---|",
    ]
    for flag in scope["businessDefinitions"]["financialStatementFlags"]:
        true_means = flag.get("trueMeans") or "Pending source verification"
        provenance = flag["provenance"] + " / " + flag["status"]
        false_means = flag.get("falseMeans") or "Not approved as a default"
        lines.append(f"| `{flag['field']}` | {flag['persian']} | {flag['english']} | {true_means} | {false_means} | {provenance} |")
    lines += ["", "The product owner confirmed `IsAudited=false` means unaudited, `IsRepresented=false` means not restated, and `IsComposing=false` means parent-company/separate; each flag is independent. These definitions apply to the Feature 141 generic path. No audited/restated default is inferred.", "",
        "## Generic statement selection policy", "",
        "Default to `IsComposing=false`; use true only when consolidated statements are requested. Return separate values for comparisons and never merge scopes. If separate data is unavailable, do not substitute consolidated data. This policy does not change CodalDb selection behavior.", "",
        "## Financial statement providers and Codal selection review", "",
        "No global provider precedence is implemented. `NoavaranCurrentApi` requests balance sheet, income statement, and cash flow endpoint families; `CyclicalWaves` currently normalizes quarterly income-statement fields only and carries stale-period warnings; `CodalDb` uses its own source-item normalization and selection policy. Preserve provider and selected revision separately.",
        f"Implemented Codal order: {scope['codalSelectionPolicyReview']['implemented']}",
        f"Conflict: {scope['codalSelectionPolicyReview']['genericPathConflict']}",
        f"Selection review: {scope['codalSelectionPolicyReview']['decisionRequired']}",
        "",
        "## Selected physical objects", "",
    ]
    table_ids = {c["table"].split(".", 1)[1]: c["id"] for c in scope["includedCandidates"]}
    for name in CURATIONS:
        record = read_json(TABLES / f"{table_ids[name]}.yaml")
        sem = record["semantic"]
        lines += [f"### `{record['physical']['Schema']}.{name}`", "", f"**English:** {sem['descriptionEn']}  ", f"**Persian:** {sem['descriptionFa']}  ", f"**Purpose/grain:** {sem['purpose']} {sem['grain']}", "", f"**Important columns:** {'; '.join(f'`{k}` — {v}' for k, v in sem['columns'].items())}", "", f"**Units:** {sem['units']}", "", f"**Relationships:** {'; '.join(sem['relationships'])}", "", f"**Provider and period policy:** {sem['providerPolicy']} {sem['periodSemantics']}", ""]
        if sem.get("businessDefinitions"):
            lines += ["**Business definitions:**", ""]
            for definition in sem["businessDefinitions"]:
                lines.append(f"- `{definition['field']}`: {definition['english']} / {definition['persian']}; true means {definition.get('trueMeans') or 'pending verification'}; false means {definition.get('falseMeans') or 'pending verification'}; provenance `{definition['provenance']}`; status `{definition['status']}`.")
            lines.append("")
        lines += [f"**Feature references:** {'; '.join(sem['metricReferences']) if sem['metricReferences'] else 'None'}", "", f"**Questions:** {'; '.join(sem['examples'])}", "", f"**Cautions:** {'; '.join(sem['cautions'])}", "", f"**Evidence:** {sem['sourceVerification']}", "", "Approval: `candidate-pending-review`; queryable: `false`.", ""]
    lines += ["## Additional external data source", "", "`api/v2/CompanyFundamentalIndex/Values` is a Noavaran provider endpoint, not a local table or public Financial Copilot controller route. The raw provider payload is retained in `ProviderRawPayloads`; mapped, scale-verified observations are normalized into `DerivedMetrics` with provider and vendor-precomputed evidence. Coverage sync uses a distinct `FundamentalIndexCoverage` dataset. See `SemanticCatalog/sources/src_nadpco_company_fundamental_index_values.yaml`.", "", "## Confirmed joins and unresolved relationships", ""]
    lines += [f"- {item}" for item in scope["relationshipsAndGaps"]]
    policy = scope["industryRelativeValuationPolicy"]
    lines += ["", "## Industry-relative valuation policy", "", f"Algorithm `{policy['algorithmVersion']}`: {policy['normalization']} {policy['benchmark']} {policy['classification']} {policy['ranking']}", f"Evidence retained: {policy['evidence']}", ""]
    lines += ["", "## Proposed initial read scope", "", f"The proposal includes {len(scope['includedCandidates'])} selected physical tables plus the one Noavaran external endpoint record. It is not approved; all physical objects and the endpoint remain non-queryable. Prefer existing metric and trend read services where direct reads could bypass provider, revision, unit, completeness, or calculation policies.", "", "### Existing support boundaries", ""]
    lines += [f"- Existing governed path: {item}" for item in scope["exampleQuestionSupport"]["answerableThroughExistingGovernedPaths"]]
    lines += [f"- Not approved for generic SQL: {item}" for item in scope["exampleQuestionSupport"]["notApprovedForGenericSemanticSQL"]]
    lines += ["", "## Remaining human review", "", "- Review the owner-supplied Persian/English ReportingType labels and million-IRR sales-amount contract before granting generic query authorization.", "- Confirm the CodalDb preference for restated statements and the `StmtId` ascending tie-break; current specialized behavior remains unchanged.", "- Verify provider DTO/source mappings preserve the owner-confirmed IsComposing meaning for each source; the generic-path default itself is IsComposing=false.", "- Review TSETMC instrument-to-company resolution collisions/provider scope and approve any future mapping-policy change separately.", "- Review semantic labels, provider eligibility, accepted revision rules, market units/source scales, and example questions before granting any queryability.", "- Resolve missing live FK constraints for monthly line items and statement line items, plus the EF/PostgreSQL uniqueness drift on statement line items.", "- Review sample coverage and the observed future-dated monthly period before making currentness claims.", "- Navigation labels and hierarchy grouping are pending review; their generated presence grants no semantic or runtime query approval.", ""]
    MARKDOWN.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    main()
