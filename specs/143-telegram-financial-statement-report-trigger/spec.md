# Feature 143 — Telegram Financial Statement Report Trigger

## User story

As a data operator, I want an authorized original `channel_post` from CodalMirror that announces a company's financial statements to trigger the existing Noavaran acquisition and persistence path, so the announcement's company and reporting period become available without manual backfill.

## Scope and dependencies

This feature extends Feature 130's single Telegram gateway and Feature 133's authorized `ChannelPost` route. It invokes Feature 142's `IManualNoavaranFinancialStatementBackfillService` directly through dependency injection; it does not call the DataAdmin HTTP endpoint. It does not create AI queries, conversations, billing events, or Telegram messages. Existing Feature 133 monthly production/sales behavior remains active and independent.

## Recognition

Recognition is deterministic and bounded to the first three lines of the Telegram text/caption. It requires a financial statement header (`صورت‌های مالی`, `صورت های مالی`, or `صورتهای مالی`), exactly one ticker hashtag, an explicit `منتهی به` period date, and one duration (3, 6, 9, or 12 months). Annual `سال مالی منتهی به` headers imply 12 months. Persian/Arabic digits, Arabic/Persian yeh and kaf, ZWNJ, and spacing variants are normalized. Jalali month/day validity is checked using `PersianCalendar`.

The recognizer reads no commentary numbers. Multiple dates/durations, invalid dates, absent tickers, mixed monthly-activity markers, and incomplete headers fail closed. It captures audited status, amendment marker, and consolidation status only when explicitly present. Omitted variant status remains unknown; configured Feature 142 provider filters are not represented as a verified match.

Examples A/B are annual 12-month announcements; C is a six-month interim announcement. In each case the reporting end date comes from the header. It is not treated as the fiscal-year ending.

## Fiscal year and provider scope

Feature 142 accepts `fiscalYearEnding` and `periodMonths`, but the normalized company catalog has no authoritative fiscal-calendar field. Feature 143 resolves a unique `JalaliFiscalYearEnd` only from persisted Noavaran statement evidence for the same external company, exact Gregorian period-end date, and matching period type. Missing or conflicting evidence yields `FiscalYearUnresolved` and no provider call. A period year, a 12-month duration, or a date mentioned in commentary is never used as a substitute.

The existing provider uses `fromYear`, `toYear`, `perTId`, and configured `isAudited`, `isRepresented`, and `isComposing` filters. These are retained by Feature 142 and the current client does not support an announcement-specific exact period-end request. `IsRepresented` is not assumed equivalent to every amendment meaning. Variant mismatch remains a limitation requiring persisted report identity/metadata verification before a requested variant can be called successful.

## Outcomes and safety

For a valid, authorized, enabled post, the handler resolves the ticker and Noavaran eligibility, resolves the fiscal year, then invokes the existing service independently for balance sheet, income statement, cash flow, and fundamental ratios. Empty/failing categories do not prevent later categories. A durable Feature 130 processed-update row is keyed by chat ID, message ID, and trigger type. It stores per-category outcomes and suppresses duplicate completed deliveries; an ambiguous claimed/in-flight post is held for review rather than blindly replayed.

The independent `ChannelFinancialStatements:AutoBackfillEnabled` flag defaults to false. When disabled, the post is durably marked skipped and the Noavaran service is not called. Channel identity and API-client/tenant authorization reuse Feature 133's channel allowlist and actor settings; for this sensitive ingestion path, the channel allowlist must be explicitly populated and an empty list rejects the post.

## Acceptance criteria

- AC-01: Only original channel posts from an authorized configured channel enter this processing boundary.
- AC-02: Deterministic recognition extracts ticker, category, exact Jalali period end, duration, and explicit variant flags from supplied formats.
- AC-03: Invalid, ambiguous, irrelevant, or mixed monthly/financial posts do not acquire data.
- AC-04: Fiscal-year resolution requires unique persisted Noavaran evidence matching company and exact period; unresolved cases do not guess.
- AC-05: The existing Feature 142 service is called once per supported category, independently.
- AC-06: Each category outcome and correlation details are durably recorded; partial results are distinguishable.
- AC-07: Duplicate and simultaneous deliveries do not repeat completed work.
- AC-08: Disabled configuration causes no provider call or financial mutation.
- AC-09: No AI orchestration, billing, conversation creation, or Telegram publication occurs.
- AC-10: Feature 133 monthly-report routing and Feature 142 manual backfill remain compatible.

## Test plan

Unit tests cover the three examples; all durations; Persian/Arabic digits; spelling/ZWNJ variants; audit/amendment/consolidation flags; invalid/missing/conflicting dates; and irrelevant/mixed monthly text. Handler tests cover disabled operation, authorization, unresolved company/fiscal year, four independent category calls, partial outcomes, and duplicate claims. Existing Feature 133 monthly tests and Feature 142 manual-backfill tests are regression checks. Provider calls are mocked; no real Telegram or Noavaran requests are made.
