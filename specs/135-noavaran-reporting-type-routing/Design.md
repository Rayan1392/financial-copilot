# Feature 135 — Design

## Current architecture

The Noavaran current-API ingestion path is provider-neutral at the application boundary and
provider-specific in Infrastructure:

```text
GET /api/v3/BaseInfo/Companies
  -> NadpcoApiDataProviderClient.FetchSymbolsAsync
  -> ProviderRawPayloads
  -> FinancialDataSyncProcessor
  -> NadpcoApiCompanyNormalizer
  -> Companies + Industries + IndustryGroups + Markets
```

Per-company monthly acquisition is bounded by `NoavaranCompanyScope`, which filters
`Companies` by `ProviderName = NoavaranCurrentApi`, `PrecedencyRight = 0`, and the three configured
market dimension IDs. `NoavaranEligibleCompanies` is an operator/read view of the same scope; its
SQL is `SELECT * FROM Companies` with that filter.

The current provider contract exposes `IMonthlyProductionSalesProvider.FetchMonthlyReportsAsync`
with only `externalCompanyId`. The Noavaran implementation is one
`NadpcoApiDataProviderClient`, not two separate manufacturing/service client classes. It currently:

- fetches ProductSales output types 0–4 from `api/v2/MonthlyActivity/ProductSales`;
- considers ProductSales output type 0 the monthly-source decision;
- when type 0 is a successful empty response, calls
  `api/v3/MonthlyActivity/ServiceSales` as a fallback;
- stores a combined raw envelope for the existing normalizer.

The direct single-company path uses the same provider boundary through
`INadpcoMonthlyProductSalesDirectProvider`; Feature 134 aligned its type-0 fallback behavior with
the scheduled path. This feature supersedes that fallback as the normal classification mechanism,
while retaining the existing normalized ServiceSales data and precedence rules for historical rows.

## Current problematic flow

```text
Company id
  -> ProductSales output type 0
       -> HTTP 200 + usable rows: ProductSales
       -> HTTP 200 + empty rows: call ServiceSales
       -> transport/invalid failure: fail/retry; do not classify
  -> ProductSales output types 1–4
  -> normalize combined envelope
  -> MonthlyReports / MonthlyReportLineItems
  -> derived metrics / trend snapshot / AI chart readers
```

This is a valid data fallback for Feature 134 but is not a durable company classification. It still
causes a second API call for a service company and cannot safely choose among future categories.

## Proposed flow

```text
Company catalog sync
  -> Companies.ReportingType

Eligible company-month request
  -> read persisted ReportingType
  -> NoavaranMonthlyReportTypeResolver
       -> Manufacturing (1000000)
            -> ProductSales output types 0–4
       -> Service (1000005)
            -> ServiceSales
       -> Missing / Malformed / Unknown / Unsupported
            -> no provider endpoint; structured outcome; retry/remediation path
  -> existing raw capture
  -> existing monthly normalizer
  -> existing persistence and recalculation
  -> existing snapshot, AI query, and chart consumers
```

The resolver is the single authoritative location for `ReportingType -> MonthlyReportProviderType`.
Workers, scheduled services, direct ingestion, and provider clients must consume its result rather
than repeat `if`/`switch` rules.

## Company synchronization data path

1. `NadpcoApiDataProviderClient.FetchSymbolsAsync` fetches and raw-stores
   `api/v3/BaseInfo/Companies`.
2. `NadpcoApiCompanyRecord` deserializes provider fields, including numeric nullable
   `ReportingType`, `MarketBoardID`, `MarketBoardTitle`, `ActivityTypeID`, and `ActivityTypeTitle`.
3. `NadpcoApiCompanyNormalizer` upserts by `(ProviderName, ExternalCompanyId)`.
4. It assigns `company.ReportingType = record.ReportingType` on both insert and update.
5. An explicit successful provider null clears the persisted value; a provider request failure does
   not reach the normalizer and therefore leaves the last successful row unchanged.
6. The existing `LastSynchronizedAt` remains the catalog freshness evidence.
7. Downstream routing reads `Companies.ReportingType`, not raw payloads and not report content.

## Noavaran Company Contract Gap Analysis

The effective comparison is:

```text
Noavaran API -> NadpcoApiCompanyRecord -> NormalizedCompanyRow -> Companies / related tables
```

The current code already maps the first 35 fields below. `industryID`, `floorID`, and `marketID`
are not stored as raw integer columns on `Companies`; they resolve provider-scoped dimension rows
and persist nullable GUID foreign keys. This is an existing verified mapping, not an inference from
identical names.

| Noavaran Field | Local Field | API DTO | DB Column | Synced | Nullable | Action |
|---|---|---|---|---|---|---|
| `coID` | `ExternalCompanyId` | `CoID: int` | `Companies.ExternalCompanyId` text | Yes | No at local identity | Keep existing key mapping |
| `coCode` | `CompanyCode` | `CoCode: string?` | `Companies.CompanyCode` text | Yes | Yes | Keep |
| `coTitle` | `Name` | `CoTitle: string?` | `Companies.Name` text | Yes | No local; fallback to symbol/id | Keep fallback |
| `coTitleEnglish` | `NameEnglish` | `CoTitleEnglish: string?` | `Companies.NameEnglish` text | Yes | Yes | Keep |
| `coSymbol` | `CompanySymbol`, `TseSymbol` | `CoSymbol: string?` | `Companies.CompanySymbol`, `Companies.TseSymbol` text | Yes | Yes | Keep existing canonical-symbol assignment |
| `coSymbolEnglish` | `CompanySymbolEnglish`, `TseSymbol` fallback | `CoSymbolEnglish: string?` | corresponding text columns | Yes | Yes | Keep |
| `floorID` | `GroupId` through `IndustryGroups.ExternalId` | `FloorID: int?` | `Companies.GroupId` FK; `IndustryGroups.ExternalId` text | Yes | Yes | Keep related-table mapping |
| `floorTitle` | `IndustryGroups.Name` | `FloorTitle: string?` | `IndustryGroups.Name` text | Yes | Yes | Keep related-table mapping |
| `industryID` | `IndustryId` through `Industries.ExternalId` | `IndustryID: int?` | `Companies.IndustryId` FK; `Industries.ExternalId` text | Yes | Yes | Keep related-table mapping |
| `industryTitle` | `Industries.Name` | `IndustryTitle: string?` | `Industries.Name` text | Yes | Yes | Keep related-table mapping |
| `tseCode` | `InstrumentCode` | `TseCode: string?` | `Companies.InstrumentCode` text | Yes | Yes | Keep |
| `tseCIsinCode` | `CompanyIsin` | `TseCIsinCode: string?` | `Companies.CompanyIsin` text | Yes | Yes | Keep |
| `tseSIsinCode` | `SymbolIsin` | `TseSIsinCode: string?` | `Companies.SymbolIsin` text | Yes | Yes | Keep |
| `marketID` | `MarketId` through `Markets.ExternalId` | `MarketID: int?` | `Companies.MarketId` FK; `Markets.ExternalId` text | Yes | Yes | Keep related-table mapping |
| `marketTitle` | `Markets.Name` | `MarketTitle: string?` | `Markets.Name` text | Yes | Yes | Keep related-table mapping |
| `precedencyRight` | `PrecedencyRight` | `PrecedencyRight: int?` | `Companies.PrecedencyRight` integer | Yes | Yes | Keep |
| `acceptionDate` | `AcceptionDateJalali` | `AcceptionDate: string?` | `Companies.AcceptionDateJalali` text | Yes | Yes | Keep source string |
| `acceptionDateGre` | `AcceptionDateGregorian` | `AcceptionDateGre: string?` | `Companies.AcceptionDateGregorian` text | Yes | Yes | Keep source string |
| `enlistedDate` | `EnlistedDateJalali` | `EnlistedDate: string?` | `Companies.EnlistedDateJalali` text | Yes | Yes | Keep |
| `enlistedDateGre` | `EnlistedDateGregorian` | `EnlistedDateGre: string?` | `Companies.EnlistedDateGregorian` text | Yes | Yes | Keep |
| `ipoDate` | `IpoDateJalali` | `IpoDate: string?` | `Companies.IpoDateJalali` text | Yes | Yes | Keep |
| `ipoDateGre` | `IpoDateGregorian` | `IpoDateGre: string?` | `Companies.IpoDateGregorian` text | Yes | Yes | Keep |
| `fundTypeID` | `FundTypeId` | `FundTypeID: int?` | `Companies.FundTypeId` integer | Yes | Yes | Keep |
| `fundTypeTitle` | `FundTypeTitle` | `FundTypeTitle: string?` | `Companies.FundTypeTitle` text | Yes | Yes | Keep |
| `coSymbolPinglish` | `CompanySymbolPinglish` | `CoSymbolPinglish: string?` | `Companies.CompanySymbolPinglish` text | Yes | Yes | Keep |
| `nationalID` | `NationalId` | `NationalID: string?` | `Companies.NationalId` text | Yes | Yes | Keep |
| `inExchange` | `InExchange` | `InExchange: int?` | `Companies.InExchange` integer | Yes | Yes | Keep source representation |
| `establishmentDate` | `EstablishmentDateJalali` | `EstablishmentDate: string?` | `Companies.EstablishmentDateJalali` text | Yes | Yes | Keep |
| `establishmentDateGre` | `EstablishmentDateGregorian` | `EstablishmentDateGre: string?` | `Companies.EstablishmentDateGregorian` text | Yes | Yes | Keep |
| `businessStartDate` | `BusinessStartDateJalali` | `BusinessStartDate: string?` | `Companies.BusinessStartDateJalali` text | Yes | Yes | Keep |
| `businessStartDateGre` | `BusinessStartDateGregorian` | `BusinessStartDateGre: string?` | `Companies.BusinessStartDateGregorian` text | Yes | Yes | Keep |
| `registrationDate` | `RegistrationDateJalali` | `RegistrationDate: string?` | `Companies.RegistrationDateJalali` text | Yes | Yes | Keep |
| `registrationDateGre` | `RegistrationDateGregorian` | `RegistrationDateGre: string?` | `Companies.RegistrationDateGregorian` text | Yes | Yes | Keep |
| `registrationNumber` | `RegistrationNumber` | `RegistrationNumber: string?` | `Companies.RegistrationNumber` text | Yes | Yes | Keep |
| `registrationProvince` | `RegistrationProvince` | `RegistrationProvince: string?` | `Companies.RegistrationProvince` text | Yes | Yes | Keep |
| `registrationCity` | `RegistrationCity` | `RegistrationCity: string?` | `Companies.RegistrationCity` text | Yes | Yes | Keep |
| `marketBoardID` | None | **Missing today**; add DTO field | None | No | Yes | Defer persistence; no current consumer or equivalent local column |
| `marketBoardTitle` | `MarketBoard` | **Missing today**; add DTO field | `Companies.MarketBoard` text | Not from supplied name | Yes | Synchronize now using title, fallback to legacy `marketBoard` |
| `activityTypeID` | None | **Missing today**; add DTO field | None | No | Yes | Provider-only/deferred; never use for routing |
| `activityTypeTitle` | None | **Missing today**; add DTO field | None | No | Yes | Provider-only/deferred; never use for routing |
| `reportingType` | `ReportingType` | **Missing today**; add `ReportingType: int?` | New `Companies.ReportingType` integer | No today; required by feature | Yes | Persist and use for routing |

### Existing model and table observations

- No separate domain/application company entity was found for this catalog; the persistence model is
  `NormalizedCompanyRow` in Infrastructure and the provider DTO is Infrastructure-local.
- `NormalizedCompanyRow` currently has `MarketBoard`, but the current DTO maps the legacy JSON name
  `marketBoard`; the supplied sample uses `marketBoardTitle`. The feature must support both names
  without overloading `MarketId`.
- `NoavaranEligibleCompanyRow` currently projects a subset of company columns. Because the database
  view uses `SELECT *`, adding a CLR property is optional for the view itself, but any route that
  resolves a company through the view must expose `ReportingType` or join/query `Companies`.

## ReportingType model

Persist `ReportingType` as `int?` / PostgreSQL `integer`:

- the supplied payload is numeric (`1000008`), not a string description;
- all known values fit a 32-bit signed integer;
- nullable storage represents an explicit missing provider value and preserves pre-backfill rows;
- raw numeric values allow unknown future codes to be retained without an enum migration;
- a code enum or resolver constants may exist in application code, but the database remains numeric.

No index is required initially. Routing reads one company by the existing provider/external-ID
identity, and an index on `ReportingType` would not improve the per-company lookup. Add one only if
future reporting-type batch operations demonstrate a measured need.

## Verified ReportingType-to-endpoint mapping

| ReportingType | Classification | MonthlyReportProviderType | Verified endpoint/client | Status |
|---:|---|---|---|---|
| `1000000` | Manufacturing / production | `ProductSales` | `NadpcoApiDataProviderClient` → `POST api/v2/MonthlyActivity/ProductSales` | Supported |
| `1000005` | Service | `ServiceSales` | `NadpcoApiDataProviderClient` → `POST api/v3/MonthlyActivity/ServiceSales` | Supported |
| `1000001` | Construction | None | No verified route in current code/docs | Unsupported |
| `1000002` | Investment | None | No verified route in current code/docs | Unsupported |
| `1000003` | Banking | None | No verified route in current code/docs | Unsupported |
| `1000004` | Leasing | None | No verified route in current code/docs | Unsupported |
| `1000006` | Insurance | None | No verified route in current code/docs | Unsupported |
| `1000007` | Maritime transportation | None | No verified route in current code/docs | Unsupported |
| `1000008` | Agriculture | `ProductSales` | `NadpcoApiDataProviderClient` → `POST api/v2/MonthlyActivity/ProductSales` | Supported |
| `1000009` | Investment banking | None | No verified route in current code/docs | Unsupported |
| any other value | Future/unknown | None | No verified route | Unknown |

## ReportingType vs ActivityType

The supplied Abin sample is deliberately important: `activityTypeID = 1` and
`activityTypeTitle = تولیدی`, while `reportingType = 1000008` (agriculture). It must resolve to
`ProductSales`, but the reason is the explicit authoritative mapping `1000008 → ProductSales`, not
the ActivityType value. This proves that activity classification is not a substitute for reporting
classification.

## Resolver outcomes and unsafe metadata behavior

| Input state | Resolver outcome | Endpoint behavior | Operational behavior |
|---|---|---|---|
| `1000000` | Supported/ProductSales | Call ProductSales only | Normal acquisition |
| `1000005` | Supported/ServiceSales | Call ServiceSales only | Normal acquisition |
| `1000008` | Supported/ProductSales | Call ProductSales only | Normal acquisition; same behavior as `1000000` |
| Known code with no verified route | Unsupported | Call neither endpoint | Record bounded diagnostic; retry only after mapping support is deployed |
| Future/unknown numeric code | Unknown | Call neither endpoint | Preserve numeric value; emit review metric/log |
| `null` | Missing | Call neither endpoint | Keep company-month unresolved/retryable; catalog backfill is required |
| malformed provider value | Malformed | Call neither endpoint for the affected metadata state | Preserve raw payload, fail or quarantine the invalid catalog record according to existing provider error policy; never guess |
| stale valid value after prior successful sync | Stale-but-usable | Use persisted value for the current run; no probing | Emit stale diagnostic; successful catalog refresh is remediation |
| provider synchronization failure | Last-known-good or Missing | Do not overwrite the last good value; if no good value exists, call neither endpoint | Existing provider failure/retry lifecycle; no reclassification |
| selected endpoint returns HTTP 200 empty | Selected-source no-data | Do not call the other endpoint | Retain existing no-data/retry semantics; empty is not classification |
| selected endpoint fails | Provider failure | Do not call the other endpoint | Existing failure/retry lifecycle |

The normal migration strategy intentionally blocks unresolved rows rather than sending them to a
guessed endpoint. If operations require a short compatibility window, it must be a separately
approved, time-bounded feature flag with explicit metrics and removal date; this specification does
not require or authorize that fallback.

## Migration and backfill strategy

1. Deploy an additive EF Core migration adding nullable `Companies.ReportingType` as PostgreSQL
   `integer`. Do not set a default and do not backfill from `ActivityType`, industry, floor, symbol,
   company name, or report contents.
2. Deploy DTO/model/upsert and resolver code.
3. Run the existing non-destructive Noavaran company-catalog refresh. Existing rows update by
   `(ProviderName, ExternalCompanyId)`; new rows are inserted as before.
4. Run a bounded catalog coverage check reporting total current-API companies, non-null reporting
   types, null values, unknown codes, and malformed/quarantined rows.
5. Complete the catalog refresh/backfill before enabling routine type-driven monthly acquisition.
6. Existing queued monthly requests created before metadata refresh must re-read metadata at worker
   execution time. Requests with no route are not marked as successfully ingested.
7. Rollback removes only the new column after routing code is disabled; because no default or
   destructive data rewrite is used, pre-existing company rows remain recoverable from the raw
   payload and catalog source.

No separate data table is required. No monthly-report schema migration is expected.

## Observability

Use the existing provider/sync logger and run-state conventions. Emit bounded structured events or
fields for:

- `ExternalCompanyId` and internal `CompanyId` when available;
- `CompanySymbol`;
- `ReportingType` (including null/unknown numeric values);
- resolver outcome and `MonthlyReportProviderType`;
- endpoint/client selected;
- catalog synchronization success/failure and metadata freshness;
- unsupported or malformed reporting metadata.

Recommended counters are `reporting_type_missing`, `reporting_type_unknown`,
`reporting_type_unsupported`, `reporting_type_malformed`, and `monthly_route_selected` partitioned
by provider type. Do not include credentials, bearer tokens, raw authorization headers, or sensitive
payloads in logs.

## Compatibility considerations

- The existing raw payload store remains unchanged.
- ProductSales normalization, output types 0–4 where applicable, `MonthlyReports`, line-item replacement, derived
  metric recalculation, and snapshot calculations remain the downstream contract.
- Agriculture companies using `ReportingType = 1000008` use the same ProductSales acquisition and
  downstream normalization behavior as `ReportingType = 1000000`.
- Existing persisted ServiceSales rows remain readable and continue to participate in the existing
  ProductSales-over-ServiceSales analytical precedence rule.
- No AI route, tool, chart schema, Telegram route, or frontend contract changes are needed.
- The direct admin and Telegram monthly-ingestion paths must use the same resolver as scheduled and
  worker-driven ingestion; they must not implement local endpoint decisions.
- `NoavaranEligibleCompanies` remains the eligibility scope, not the classification authority. The
  classification authority is `Companies.ReportingType`.

## Affected components/files

Expected implementation touch points (not modified in this specification task):

- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiPayloadModels.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiCompanyNormalizer.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/Migrations/*`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiScheduledSyncService.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityBackfillCoordinator.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/SingleCompanyMonthlyIngestionService.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence` view/read-model code,
  only if direct lookup requires `ReportingType` on the view projection.
- focused unit/integration tests for company normalization, provider routing, sync processing, and
  direct ingestion.

No changes are planned for AI semantic routing, chart rendering, or Telegram business behavior.

## Test strategy

### Company synchronization

- DTO deserialization with numeric `reportingType`, null, unknown future numeric, and malformed input.
- New-row persistence and existing-row update.
- Explicit null clearing after a successful catalog response.
- Failed catalog fetch leaves last successful row unchanged.
- Complete sample field regression, including existing identity/dimension/date mappings.
- `marketBoardTitle` mapping and legacy `marketBoard` fallback.
- ActivityType/ReportingType distinction using the Abin sample.
- Migration model validation: nullable integer, no default, no unexpected index.

### Resolver and provider routing

- Table-driven resolver tests for all ten listed codes plus null, unknown, and malformed outcomes;
  `1000000` and `1000008` resolve to ProductSales, while `1000005` resolves to ServiceSales.
- Manufacturing request asserts ProductSales was called and ServiceSales was not called.
- Agriculture request using the Abin fixture asserts ProductSales was called and ServiceSales was
  not called.
- Service request asserts ServiceSales was called and ProductSales was not called.
- Remaining unsupported-code fixtures assert neither endpoint was called.
- Empty HTTP 200 from the selected endpoint does not call the other endpoint.
- Transport, authorization, timeout, and invalid-payload failures remain failures.
- Repeated synchronization uses stable idempotency and does not duplicate reports.
- Scheduled, direct admin, and Telegram-triggered paths all use the same resolver.

### Regression

Run the focused tests for specs `039`, `042`, `053`, `057`, `059`, `076`–`078`, and `134`, plus the
full relevant backend solution tests. Verify normalized monthly sales, derived metrics, trend
snapshots, AI monthly-sales query responses, and chart payloads remain compatible. If infrastructure
or provider access prevents a live smoke test, record the blocker and preserve deterministic fixture
coverage.

## Design decisions and unresolved questions

### Decisions

1. Store raw numeric `ReportingType` as nullable `int`, not a persisted enum or description.
2. Use one resolver as the only mapping from code to monthly provider type.
3. Support manufacturing/ProductSales, agriculture/ProductSales, and service/ServiceSales because
   those are the mappings established by the current business/provider requirement.
4. Treat null, unknown, malformed, and unsupported values as no-route; never guess.
5. Use the persisted last-known-good value after a failed catalog refresh, with staleness telemetry.
6. Preserve the existing normalized monthly-report schema and downstream analytical precedence.
7. Synchronize `marketBoardTitle` into the existing `MarketBoard` text field, but defer
   `marketBoardID` and ActivityType persistence because no current consumer or equivalent local
   model is established.

### Unresolved questions / blockers

- Confirm with Noavaran whether `marketBoardTitle` is the current canonical replacement for the
  legacy `marketBoard` property across all catalog responses; fixture coverage should include both.
- Confirm the operational freshness threshold, if one is later needed to block stale values rather
  than merely warn. This design does not add a new threshold.
- Confirm whether Noavaran will ever return `reportingType` as a quoted numeric string. If so, the
  provider parser needs an explicit tolerant contract rule while preserving malformed-value
  diagnostics.
- Provider documentation does not establish monthly endpoints for codes outside the three mappings
  above. The listed codes not included above remain intentionally unsupported until evidence is supplied.
