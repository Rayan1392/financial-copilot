# Feature 148 — CyclicalWaves v2 Independent Financial Data Provider

Status: **Original Feature 148 implemented and closed as `IMPLEMENTED_WITH_KNOWN_CHAT_ROUTING_LIMITATIONS` (2026-10-31). Gauge and Equilibrium extension implementation was authorized by the Product Owner on 2026-11-08 and is in progress.**

Product Owner decisions are final:

| Item | Decision |
|---|---|
| D1–D9 | As in [design.md §13](./design.md#13-product-owner-decisions-final) |
| C1 | **B**: exclude legacy CyclicalWaves-derived metrics from NoavaranAmin lookup and scanner reads |
| C2 | **C**: comprehensive analysis is disabled in NoavaranAmin mode |
| C3 | **A**: basic screening over CyclicalWaves v2 data |

The authoritative contracts are `tahlilapp-api-client.pdf` (API v2.0) and `income-statement-api-client.pdf` (2 Aban 1405). They will be committed under `docs/providers/cyclicalwaves-v2/` (task T148-00.1).

## 1. Business objective

The legacy CyclicalWaves server (`https://back1.cyclicalwaves.com/api`) is discontinued. Feature 148 does the following:

1. Retires **all 11** legacy endpoints at runtime, together with the schedules and Admin triggers that call them. Legacy records are retained.
2. Integrates the **7** v2 endpoints and persists their data in the independent `cyclicalwaves` schema.
3. Synchronizes daily, and on demand from Admin.
4. Lets Chat use v2 data only when `FinancialData:ChatProvider=CyclicalWaves`. This covers lookup, quarterly statements, comprehensive analysis and basic screening, with no provider mixing or fallback.
5. Disables capabilities that depend only on discontinued endpoints.
6. Changes NoavaranAmin only as approved in C1 and C2.

## 2. Functional requirements

### Ingestion

Ingestion is independent of `ChatProvider`.

- **FR-1 Ticker catalog** (`GET /custom-filtering/tickers`).
  - New tickers are inserted once.
  - Tickers absent from the response are marked inactive and never deleted.
  - The ISIN (`enticker`) is learned from the screener response.
- **FR-2 Metric definitions.** 29 definitions are seeded deterministically, each with field key, English name, Persian name, category and unit (`IRR`, `Percent` or `Ratio`).
- **FR-3 Screener values** (`GET /custom-filtering/ticker/{ticker}`).
  - Latest state only: one row per ticker and metric, with foreign keys to the ticker and the definition.
  - A row is written only when its value changed. `null` is preserved.
  - `lastMonthSaleDate` and `lastQuarterDate` are preserved.
  - No snapshots or history are kept.
- **FR-4 Line definitions** (`GET /income-statements/lines`). Store the provider's numeric key plus the English and Persian names.
- **FR-5 Income statements** (`GET /income-statements/ticker/{ticker}?limit=N`, default 12, configurable).
  - Each statement keeps its period identity, fiscal year and month, audited flag, publish date and Codal URL.
  - Each line value keeps quarter, year-to-date and trailing-year figures, with a foreign key to the line definition.
  - Rows are upserted. Only reported periods are stored.
- **FR-6 Comprehensive analysis** (`GET /blogs?filter[type]=analysis`).
  - Store id, title, summary, `published_at`, categories and tags.
  - Deduplicate by provider id, update an article when it changes, and never delete one.
  - The first load reads all pages. Daily sync is incremental: `filter[from_date]` with a 2-day overlap, paginated up to `meta.last_page`.
  - Only documented fields are stored. Article bodies are not assumed to exist.

### Operations

- **FR-7 Daily worker.**
  - Configurable UTC cron, default `30 23 * * *`.
  - Order: catalog, then line definitions, then screener and statements per active ticker, then analysis.
  - Runs a startup catch-up after a missed day.
  - Uses a single global lease, so runs never overlap.
  - Basic bounded retry.
- **FR-8 Admin panel** on the existing Data Management console, restricted to `DataAdmin`:
  - Inline: catalog sync and single-ticker sync.
  - Queued and run by the Worker: screener sync, statement sync, analysis sync (incremental or full), and full sync.
  - Run status, counts and errors.
- **FR-9 Run tracking.** Each run records scope, trigger, status, timestamps, item counts and a bounded error summary.
- **FR-10 Authentication.**
  - Login sends `x-client-id: default` with `identifier` and `password`; refresh sends `refreshToken`.
  - The refresh token rotates.
  - The access token is renewed ahead of expiry, based on the JWT `exp` or a 4-minute fallback.
  - A 401 triggers one refresh and one retry. There is no login loop.
  - Credentials come from the environment or secrets only.
  - Tokens are held in memory per process.

### Chat provider selection

- **FR-11 Configuration.** `FinancialData:ChatProvider` is `NoavaranAmin` (default) or `CyclicalWaves`.
  - It is validated at startup.
  - It controls Chat consumption only and never affects ingestion.
- **FR-12 CyclicalWaves mode.** Every read comes from `cyclicalwaves` tables:
  - ticker resolution, metric lookup and quarterly statement tables
  - comprehensive analysis (title, summary, date, categories, tags)
  - basic screening (FR-14)

  Anything else returns an explicit unavailable-data answer. Legacy CyclicalWaves records and NoavaranAmin records are never read, and there is no fallback or merge.
- **FR-13 NoavaranAmin mode.** The existing path is kept, apart from three changes:
  - **(a) C1:** legacy CyclicalWaves-derived `DerivedMetrics` rows are excluded at the read boundary of symbol lookup, the scanner and the professional scanner. The discriminator is the structured `FinancialSourceEvidence.SourceProvider` (FR-17).
  - **(b) C2:** comprehensive analysis is disabled. Neither the legacy nor the v2 analysis tables are read, and the answer is an explicit unavailable message.
  - **(c)** The retired capabilities in FR-16 are disabled.

  Ingestion, persistence, calculation formulas and every other NoavaranAmin behavior stay unchanged.
- **FR-14 Basic screening (CyclicalWaves mode, C3).** The existing scanner parser, plan validator, ranker and column policy are reused with a CyclicalWaves executor.
  - **Metrics:** P/E, P/S, monthly sales, 12-month average monthly sales, quarterly sales, quarterly net, gross and operating profit, and the three margins.
  - **Growth:** quarter-over-quarter, year-over-year (quarter), month-over-month, and month versus the same month last year. Each is computed only from documented operand pairs, using the existing growth policy (non-positive base means unavailable).
  - **Operators:** `>`, `>=`, `<`, `<=`, `=`, `≠`. "Between" is the parser's pair of bound conditions.
  - **Monetary thresholds:**
    - Units are resolved deterministically from the user's text: ریال, تومان, هزار, میلیون, میلیارد, هزار میلیارد and همت.
    - Thresholds are converted to IRR, with toman = 10 IRR.
    - A non-zero monetary threshold with no stated unit gets a clarification request. No unit is ever assumed.
  - **Percent thresholds:** margin and growth thresholds are percentages.
  - **P/E of zero:** `pe = 0` is "not computable" and never matches a P/E condition.
  - **Missing values:** `null` values never match and are reported as missing.
  - **Unavailable criteria:** industry filters, balance-sheet, production, quantity, EBIT and EPS growth, and any other undocumented factor. These return an explicit unavailable answer.
- **FR-15 Units.** Money (including EPS) is stored in IRR, margins as percent, and P/E and P/S as ratios. Conversion happens only when presenting a response or interpreting a user threshold.

### Legacy retirement and C1

- **FR-16 Legacy retirement.**
  - A handler-level guard on every legacy client blocks every request before transmission.
  - Legacy scheduled workers are unhosted: acquisition, comprehensive-analysis daily sync, and the Feature 125 calculation worker.
  - Legacy Admin ingestion triggers return `410 Gone` without calling any service.
  - Legacy-only capabilities are disabled in both modes:
    - the P/S gauge (115) and its producer (114)
    - industry relative valuation (125) and its producers (126, 127)
    - the comprehensive-analysis-published insight detector
  - Legacy records are not deleted, truncated, rewritten, migrated or backfilled.
  - Superseded specifications get a header note only.
- **FR-17 C1 discriminator (with C1-M, permanent).**
  - A row is legacy CyclicalWaves-derived when any item in its `SourceEvidenceJson` (or its dependency evidence) has `SourceProvider = "CyclicalWaves"`. This includes **mixed** rows, where one operand is CyclicalWaves and another is NoavaranAmin (decision C1-M, 2026-10-31).
  - A row that records no provider is legacy when its registered `CalculationPolicyVersion` reads only a vendor line item that the legacy CyclicalWaves normalizers alone produce (`cw-avg-12m-monthly-sales-passthrough-v1`, `cw-avg-4q-revenue-passthrough-v1`, `vendor-pe-ratio-passthrough-v1`, `vendor-ps-ratio-passthrough-v1`).
  - A row whose evidence cannot be parsed is excluded, because its provenance cannot be established.
  - Values, dates, tickers and substring heuristics are not used.
  - The exclusion is unconditional: there is no configuration switch, and no setting can include legacy or mixed rows again. Excluded rows are never modified or deleted.
  - It applies to every NoavaranAmin `DerivedMetrics` read that selects financial evidence: symbol lookup, both scanner queries, the professional scanner's availability and freshness check, and financial-metric alerts (N1).

## 3. Out of scope

- Any NoavaranAmin change beyond C1 and C2, including:
  - ingestion, persistence, catalogs and formulas
  - `CompanyResolverService`
  - `ConditionalTrackerEvaluationProcessor`. It also reads `DerivedMetrics`, but it is not in the C1 authorization; see impact-analysis §6.
- Rebuilding legacy-only datasets: gauges, equilibrium, prediction, P/S history.
- Article bodies, undocumented endpoints, embeddings, vector search or RAG frameworks.
- Screener history or snapshots, and revision subsystems.
- Cross-provider migration or fallback, and merged persistence.
- Feature 141 over `cyclicalwaves`.
- A new general-purpose scanner engine.
- Industry screening.
- Product-level monthly production and sales, and monthly trend charts.
- Deleting legacy code or tables, and unrelated cleanup.

## 4. Acceptance criteria

| # | Criterion |
|---|---|
| AC-1 | With default config, every legacy client request from any entry point returns `ProviderRetired` and the inner HTTP handler records zero sends. |
| AC-2 | An architecture test proves that every legacy typed client carries the guard and that the three legacy workers are not hosted. Legacy Admin ingestion triggers return `410` and invoke no service. |
| AC-3 | Legacy table row counts are identical before and after the migration and a v2 Full run. |
| AC-4 | Catalog sync inserts each ticker once. A repeat inserts nothing. A missing ticker becomes inactive and keeps its data. |
| AC-5 | `MetricDefinitions` holds exactly 29 rows, each with key, `NameEn`, `NameFa`, category and unit. |
| AC-6 | Each `FinancialValues` row has foreign keys to its ticker and definition. `null` is stored as SQL NULL. Metadata dates equal the response. |
| AC-7 | An identical response changes no row. A changed value or date updates only the affected rows. No duplicate (ticker, metric) row is created. |
| AC-8 | Line definitions keep keys and both names. An unknown key in a statement creates its definition from the line's `key`/`name`/`nameFa`. |
| AC-9 | N periods produce N statements, each with one value per line. A re-sync creates no duplicates. Changed values update in place. Absent periods are not deleted. |
| AC-10 | Analysis articles are stored once per provider id with categories and tags. Overlapping syncs insert no duplicates, changes update in place, and nothing is deleted. |
| AC-11 | The daily schedule and the startup catch-up run the full sequence. A concurrent run is recorded as `SkippedAlreadyRunning` and makes no provider call. |
| AC-12 | Admin: inline actions return results. Queued actions return `202` with a run id that the Worker completes. A conflict returns `409`. Status shows runs and errors. Non-`DataAdmin` callers get `403`. |
| AC-13 | An expired token causes exactly one refresh and one retry, and the rotated refresh token is persisted in memory. |
| AC-14 | In CyclicalWaves mode, the P/E, monthly-sales, quarterly-table and analysis questions are answered from `cyclicalwaves` only. Spy NoavaranAmin ports and legacy repositories record zero calls. |
| AC-15 | In CyclicalWaves mode, unsupported questions return the explicit unavailable answer, with no fabricated value and no other provider read. |
| AC-16 | Analysis answers show title, summary (when present) and date, and never claim the full article was read. |
| AC-17 | In NoavaranAmin mode, comprehensive-analysis questions return the explicit unavailable answer, and no legacy or v2 analysis table is queried. |
| AC-18 | C1 lookup and scanner tests:<br>• Legacy CyclicalWaves-evidenced rows are never selected.<br>• Valid NoavaranAmin rows remain selectable.<br>• A metric with no valid row yields Missing or unavailable, never a stale value.<br>• Row counts in `DerivedMetrics` are unchanged.<br>• NoavaranAmin ingestion tests pass unchanged. |
| AC-19 | In NoavaranAmin mode, all other existing Chat, lookup, scanner, quarterly-table and AI-facade regression suites pass. The only expected differences are covered by AC-17, AC-18 and AC-21. |
| AC-20 | The v2 worker and the NoavaranAmin workers behave identically under both `ChatProvider` values. |
| AC-21 | In both modes, P/S gauge and industry-relative-valuation questions return the disabled-capability answer. The comprehensive-analysis-published detector emits no events. |
| AC-22 | CyclicalWaves scanner:<br>• Direct P/E, P/S, profit, margin and sales filters return correct rows.<br>• `pe = 0` and null never match.<br>• "Between" works.<br>• Monetary thresholds in ریال, تومان, میلیون, میلیارد and هزار میلیارد or همت convert to the correct IRR.<br>• A unitless monetary threshold triggers clarification.<br>• Growth QoQ, YoY, MoM and same-month results match hand-computed fixtures, including non-positive bases.<br>• Unsupported criteria return the unavailable answer.<br>• Zero NoavaranAmin or legacy reads occur. |
| AC-23 | No foreign key connects the `cyclicalwaves` schema to any other schema (checked via `information_schema`). |
| AC-24 | No production code, migration, configuration or data change was made before implementation approval. This is a process gate. |
| AC-25 | NoavaranAmin ingestion, Feature 139, Telegram transport, billing and other unrelated suites keep passing. |

## 5. Definition of Done

- Every `tasks.md` task is checked, with evidence.
- All AC tests pass, apart from pre-existing unrelated failures, which are listed.
- The migration's `Up` and `Down` are verified on disposable PostgreSQL 18.
- The C1 census result is recorded.
- Docs and spec headers are updated, and no secrets are committed.
- `ImplementationReport.md` states whether live provider verification was executed (with evidence) or not executed.

## Post-Closure Scope Extension — Gauge and Equilibrium APIs

**Extension status:** `FEATURE_148_GAUGE_EQUILIBRIUM_PARTIALLY_VERIFIED` (2026-11-08). Implementation and scripted unit/API/Chat checks are in place; PostgreSQL migration/round-trip, live-provider and external-model verification remain pending.
The Product Owner authorized implementation on 2026-11-08 by requesting that the remaining Feature 148 items be implemented. The original Feature 148 closure status, known Chat-routing limitations, test evidence and partial T148-08.4 remain unchanged.

### Objective and provider contract

Extend the existing Feature 148 provider with four read-only data endpoints from the authoritative
[`gauge-equilibrium-api-client.pdf`](../../../docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-client.pdf):

| Endpoint | Purpose | Contract summary |
|---|---|---|
| `GET /pe-ps-gauges/tickers` | Gauge catalog | Alphabetical ticker/ISIN entries with independent `pe` and `ps` availability flags. |
| `GET /pe-ps-gauges/ticker/{ticker}` | Gauge details | Nested nullable P/E and P/S gauge records; `a`–`f` are historical day counts, ratio fields are decimals, and `status` is nullable. |
| `GET /equilibrium-ranges/tickers` | Equilibrium catalog | Alphabetical ticker/ISIN entries. |
| `GET /equilibrium-ranges/ticker/{ticker}` | Equilibrium details | IRR `min`, `avg`, `max`; negative values are valid and `avg` is the midpoint. |

Use the existing auth server and credentials. The API host is independently configurable as
`CyclicalWavesV2:GaugeBaseUrl`, defaulting to `https://api.tahlilapp.com/api`; retain
`CyclicalWavesV2:DataBaseAddress` unchanged. Both hosts use the same authorized account, singleton
token service and refresh-token rotation lifecycle; add no credentials or login flow.

### Storage proposal

Use only schema `cyclicalwaves` and the existing `cyclicalwaves.Tickers` rows. Do not add another
symbol master or any cross-schema foreign key. Add latest-state tables:

- `GaugeAvailability`, one row per `TickerId`, storing catalog `HasPe`, `HasPs`, and `LastFetchedAtUtc`.
- `GaugeDetails`, one row per `(TickerId, GaugeType)` where type is `PE` or `PS`; store `A`–`F` as
  integer day counts, `Close` as nullable exact decimal, required ratio-axis/summary values `Start`,
  `End`, `Min`, `Avg`, `Max` as exact decimals, nullable status, and fetch time.
- `EquilibriumRanges`, one row per `TickerId`, with `HasRange`, nullable IRR `Min`, `Avg`, `Max`, and
  fetch time. `HasRange=false` records catalog absence while retaining last-known values; null bounds
  are allowed only before any successful range fetch.

Use unconstrained PostgreSQL `numeric` columns (no float/double or arbitrary precision/scale limit).
EF Core/Npgsql mappings must explicitly use PostgreSQL `numeric`, without routing provider values
through binary floating point. The implementation represents each logical decimal as three base-10
`numeric` coefficient chunks plus sign and scale, then reconstructs the exact value for JSON and Chat.
Chunking keeps each stored integer within PostgreSQL's per-value numeric digit limit. .NET `decimal`
is bounded, while the PDF sets no maximum magnitude or scale, so an implementation spike must verify
exact base-10 parsing, storage and serialization. Unique
constraints enforce `(TickerId, GaugeType)` and `TickerId`; indexes support type/status and availability
queries. FKs point only to `cyclicalwaves.Tickers`. Retain latest state only; preserve original Feature
148 history and `SyncRuns`, and add no gauge history/time-series tables.
Catalog absence marks availability false but retains the last detail values; consumers must honor
availability and not display retained values as current. A detail 404 against a listed item marks
that item unavailable and is reported without writing zero or deleting the last detail row.

Keep documented nulls as SQL `NULL`; never substitute zero. A missing `pe`/`ps` nested object means no
detail row for that gauge type. A present gauge can have `Close = NULL` and `Status = NULL`. A detail
404 is a no-data/error outcome, never a zero-valued success. Negative equilibrium values are valid.
Do not enforce an exact midpoint database check until provider rounding precision is documented.

### Ticker resolution and synchronization

For catalog records, resolve against the existing catalog by case-insensitive trimmed ISIN first;
otherwise normalize Arabic/Persian Yeh and Kaf and match the normalized ticker using the existing
Feature 148 normalizer. Require exactly one matching `Tickers` row. Do not insert a ticker from these
catalogs. Unmatched or ambiguous entries are skipped and counted with a sanitized warning; they do
not silently create duplicates. For detail requests use the matched row's ISIN when present, otherwise
its canonical Persian ticker, and apply URL encoding exactly once.

Extend the existing sync scope enum, coordinator, `SyncRuns` lease and `CyclicalWavesV2SyncWorker`.
The proposed full order is: main ticker catalog; statement line definitions; existing screener and
statement sync; gauge catalog and eligible gauge details; equilibrium catalog and eligible range
details; analysis sync. Daily execution remains once per existing UTC schedule. Admin gets queued
gauge-catalog, full-gauge, equilibrium-catalog, full-equilibrium and optional single-ticker scopes.
Use the same global lease, request pacing, bounded transient retries, status counts and capped error
summary. Report each phase separately, including matched, unmatched, updated, unchanged, missing and
failed counts. Catalog failure is fatal for that phase; per-symbol detail failures continue and are
reported. No undocumented rate limit is assumed.

### Approved capability disposition

P/E Gauge, P/S Gauge, and Equilibrium Price Range are available in Chat only when
`FinancialData:ChatProvider=CyclicalWaves`, reading only new CyclicalWaves tables. There is no
NoavaranAmin fallback. In NoavaranAmin mode, these tables are never read and existing behavior stays
unchanged.

Chat distinguishes current gauge close, historical min/avg/max, axis start/end, and status; preserves
nulls; treats a-f as day counts; and states equilibrium values in IRR. Status 1 is below min, 2 between
min and avg, 3 between avg and max, 4 above max, and null means close is unavailable or on a boundary.
Below-min screening may use status 1; below-average screening compares non-null close < avg and
excludes nulls. Unsupported or missing data returns an explicit unavailable answer. Equilibrium ranges
are neither guaranteed future prices nor investment recommendations.

Restore only Feature 115's standalone P/S gauge display after adapting it to the latest-state contract.
Do not restore its historical P/S chart, monthly-sales inset, LastPS/TTM, Forward P/S, or other old
overlays. P/E and equilibrium have no corresponding old standalone UX. Features 114, 125, 126, and 127
remain disabled; no industry comparison, forward prediction, legacy sync pipeline, or old payload is
restored.

### Acceptance criteria for the extension

- Four documented GET routes use their exact path, bearer auth, separate gauge-data base address,
  shared auth credentials and shared token-refresh owner.
- A concurrent 401 across both hosts cannot race refresh-token rotation; only one renewal occurs for
  the rejected access token, followed by at most one replay per request.
- Fixtures cover catalog/detail payloads, nullable gauge objects, nullable close/status, six integer
  day counts, negative equilibrium values, 401 and 404.
- Persian ticker names are encoded once; ISIN matching is case-insensitive; Arabic/Persian spelling
  variants resolve to one canonical Feature 148 ticker or are reported as ambiguous.
- All tables and FKs remain in `cyclicalwaves`; each new row references the existing ticker identity;
  no historical legacy row or NoavaranAmin table is changed.
- Decimal values preserve JSON decimal precision; nulls remain null; negative equilibrium bounds are
  accepted; invalid or incomplete required fields are reported without fabricating values.
- Repeat syncs create no duplicate rows and do not rewrite unchanged values. Missing catalog matches,
  provider 404s and partial failures are visible in run counts and bounded errors.
- Daily and manual sync share the existing worker, sync-run table and global lease; no overlapping
  run or second daily worker is introduced.
- Admin actions remain DataAdmin-protected; long-running actions are queued and status counters are
  accurate. Single-ticker actions resolve against the canonical ticker catalog.
- Chat tests prove all three capabilities work only in CyclicalWaves mode, NoavaranAmin reads none of
  the new tables, Persian semantic variants route correctly, and unavailable responses do not fallback.
- Compatibility tests cover scoped Feature 115 standalone P/S gauge and reject unsupported
  historical/forward/monthly components. Features 114, 125, 126, and 127 stay disabled.
- The migration is additive and down/up verified on disposable PostgreSQL; no live provider
  verification is claimed unless actually executed and evidenced.

Approved Chat semantic acceptance examples (examples, not literal routing rules):

- وضعیت گیج P/E فولاد چگونه است؟
- وضعیت گیج P/S کگل چیست؟
- P/E فولاد نسبت به میانگین تاریخی خودش در چه وضعیتی قرار دارد؟
- مقدار فعلی P/S فولاد نسبت به محدوده تاریخی آن کجاست؟
- حداقل، میانگین و حداکثر قیمت تعادلی کچاد چقدر است؟
- قیمت تعادلی فولاد چقدر است؟
- کدام نمادها در وضعیت P/E زیر حداقل تاریخی قرار دارند؟
- کدام نمادها در وضعیت P/S زیر میانگین تاریخی قرار دارند؟
