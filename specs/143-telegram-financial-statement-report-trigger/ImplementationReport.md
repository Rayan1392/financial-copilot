# Feature 143 Implementation Report

## Status

Implemented. Focused tests, Feature 133/142 regressions, and the Release solution build passed. The full solution test run completed with pre-existing failures across unrelated AI, scanner, semantic-routing, admin, and market-insight areas. No live Telegram or Noavaran call was made.

## Feature path and files

Feature directory: `specs/143-telegram-financial-statement-report-trigger/`.

Created:

- `spec.md`, `design.md`, `tasks.md`, `ImplementationReport.md`.
- `src/backend/FinancialCopilot.Application/Telegram/TelegramFinancialStatementRecognizer.cs` — parser, recognition record, and recognizer contract.
- `src/backend/FinancialCopilot.Application/Telegram/TelegramFinancialStatementContracts.cs` — handler contract.
- `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramChannelFinancialStatementOptions.cs` — independent feature flag.
- `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramChannelFinancialStatementHandler.cs` — channel authorization, durable claims/checkpoints, fiscal-year resolution, validation, four category calls, and logs.
- Parser and handler unit tests in `tests/FinancialCopilot.UnitTests/`.

Modified:

- `TelegramAssistantController` dispatches candidate financial posts to the new handler before falling back to the existing Feature 133 monthly handler.
- Infrastructure DI registers the option, recognizer, and handler.
- `appsettings.json` sets `ChannelFinancialStatements:AutoBackfillEnabled` to `false`.
- `specs/README.md` lists Feature 143.

There is no database migration: claims and category checkpoints use the existing unique, durable `auth_telegram_processed_updates` table. The working tree already contained uncommitted Feature 142 changes before this feature; those files were preserved and reused.

## Integration and behavior

Feature 130's `PrimaryApiClient` and Telegram gateway remain the transport. Feature 133's `TelegramChannelMonthlyReportHandler` remains the fallback and was not modified. The controller passes the authenticated channel actor to both routes. The new handler reuses Feature 133's allowed channel IDs, authorized API-client ID, and tenant ID. Telegram posts do not require `Message.From`; the existing API contract still requires channel user ID zero and `chatType=channel`.

The parser normalizes Arabic/Persian digits and yeh/kaf, ZWNJ, and spacing; it reads only the structured announcement header among the first three lines. It validates the Jalali date and rejects conflicting period dates/durations. It detects monthly activity mixed with financial statements and consumes that ambiguous message without allowing it to reach the monthly pipeline.

- Example A parses `زنده رود`, annual 12-month, `1403/12/30`, unaudited, original. The explicit annual header supplies a candidate fiscal-end date; each fetched category must then persist the exact period and fiscal-end evidence to count as verified.
- Example B parses `قلرست`, annual 12-month, `1405/04/31`, unaudited, amended. Since the provider's `isRepresented` semantics are not established as equivalent to the Telegram amendment label, the results are marked `ReviewRequired` rather than reported as an imported matching amendment.
- Example C parses `چخزر`, interim six-month, `1405/06/31`, unaudited. Fiscal-year ending is resolved only from exact matching existing Noavaran period evidence; otherwise the durable outcome is `FiscalYearUnresolved` and there is no provider call.

Company resolution uses `ICompanyResolverService` followed by the existing `NoavaranEligibleCompanies` scope. The handler calls `IManualNoavaranFinancialStatementBackfillService` once for each of Balance Sheet, Income Statement, Cash Flow, and Financial Ratios. Feature 142 retains the provider client, query construction, ingestion processor, normalizers, persistence, and ingestion-level idempotency.

The Noavaran request filters on the Feature 142 fiscal-year and `perTId` contract. Because the provider has no exact period-end request parameter, the handler verifies persisted company, period end, period type, fiscal-year evidence, and explicit audited/consolidated metadata before treating a category as persisted. For amendments, `ReviewRequired` is retained even if data for the requested period was acquired. No unrelated persisted data is deleted during this validation.

## Idempotency, retries, and observability

The stable trigger key is based on chat ID, Telegram message ID, and the financial-statement trigger type. A unique insert claim suppresses duplicate and simultaneous deliveries. Each category outcome is checkpointed after its call. A later delivery of a partial result atomically claims retry work and reprocesses only unsuccessful categories, up to three attempts; completed categories are skipped. A claimed/processing row after interruption is treated as ambiguous and held for review, not replayed blindly. This provides durable duplicate protection with Feature 142's normalizer idempotency; it does not claim distributed exactly-once execution.

Structured logs include channel/message identity, ticker, external company ID, report category, period end, fiscal-year ending, duration, variant flags, category outcome/count, reason, and correlation ID. Tokens and credentials are not logged. The handler does not invoke AI, billing, conversation creation, or Telegram send operations.

## Configuration and operations

`ChannelFinancialStatements:AutoBackfillEnabled` is independent and false by default. Configure the existing `ChannelMonthlyReports:AllowedChannelIds` with CodalMirror's signed chat ID, and configure the authorized API client and tenant for the gateway actor as already required by Feature 133. An empty allowlist rejects Feature 143 posts. Enable the new flag only through explicit production configuration after deploying migrations already required by Features 130/142.

Smoke test with one authorized original `channel_post` after confirming exact company/period/fiscal-year evidence. Verify four per-category outcomes and persisted report identity in logs/database. Repeat the same Telegram message to confirm replay suppression. An interim post without authoritative fiscal-year evidence should produce `FiscalYearUnresolved` and no Noavaran request. An amendment should remain `ReviewRequired` until provider variant mapping is confirmed.

## Verification

- Parser + Feature 143 handler + Feature 133 channel handler + Feature 130 gateway tests: **53 passed**, including fiscal-evidence, partial-retry, duplicate replay, explicit allowlist, empty-allowlist, Arabic-Indic digit, and standalone-marker cases.
- NADPCO provider tests: **41 passed**.
- Feature 142 Noavaran statement-backfill endpoint integration tests: **6 passed**.
- `dotnet build src/backend/FinancialCopilot.sln --configuration Release --no-restore`: **passed**, 0 warnings and 0 errors.
- Full `dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore --verbosity quiet`: Architecture **12/12 passed**; Unit **2,100 passed, 18 failed**; Integration **451 passed, 62 failed, 44 skipped**. Failures are in unrelated AI observability/model providers, Feature 141 semantic query, Feature 128 routing, scanner, Telegram account/membership, market insight, and existing admin/PostgreSQL integration areas. Feature 143 tests and the targeted Feature 133/142 regression tests passed independently.

## Remaining limitations

- The company catalog has no authoritative fiscal-calendar column. Interim announcements without exact-period Noavaran fiscal-year evidence are intentionally skipped for review.
- The provider's current request contract cannot target exact period-end or override announcement variants. Exact persisted report identity is checked after ingestion; mismatches become `ReviewRequired`. `isRepresented` is not equated with amendment.
- No external provider or Telegram operation was run, and no production smoke test was performed.
