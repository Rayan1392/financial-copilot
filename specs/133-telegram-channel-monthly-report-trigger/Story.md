# Feature 133 — Story

## 1. Story

As an operator of an authorized Telegram financial channel integration, when a supported monthly activity report is posted, I want FinancialCopilot to refresh the corresponding company and Shamsi month and, when enabled, publish the existing production/sales trend analysis to the same channel, so followers receive current analysis without manual intervention.

## 2. Business Value

The feature turns an approved channel publication into a bounded, repeatable refresh and analysis flow. It uses current persisted financial capabilities, avoids stale publication, preserves tenant billing controls, and removes manual backfill and reposting work.

## 3. Trigger and Preconditions

- The event is an original Telegram `channel_post` from an authorized signed channel, with a positive message ID and channel chat ID.
- Text or a textual media caption contains a bounded supported monthly activity report marker and one unambiguous company and Shamsi period.
- Edited posts, unsupported media-only posts, irrelevant posts, and unresolved or ambiguous fields do not enter ingestion.
- The existing gateway API client, credential, tenant assignment, HTTPS path authorization, and channel allowlist are configured.
- Feature 133 uses the two global primary API options described below; both default to `false`.

## 4. Main Success Flow

1. Existing `FinancialCopilot.TelegramGateway` receives and maps the original channel post, preserving the signed chat ID, message ID, text/caption, and correlation.
2. The backend authorizes the configured gateway API client and deterministically extracts and validates the canonical company, Shamsi year, and month.
3. The handler claims a stable post key and evaluates the configuration gates.
4. When backfill is enabled, it captures `refreshStartUtc` and calls the existing direct single-company/month ingestion service.
5. It requires a completed, error-free run and a requested-period trend snapshot whose company, year, month match and whose `CalculatedAtUtc >= refreshStartUtc`.
6. When publication is enabled, it invokes the existing `روند تولید و فروش {symbol}` capability through the selected orchestration path, with the authenticated API-client identity and existing organization billing resolver.
7. It validates company/period, creates an isolated conversation, renders the existing response, and sends all parts to the originating channel only.
8. Durable post and outbound-part state supports approved replay and bounded retry behavior.

## 5. Configuration Behavior

`ChannelMonthlyReports:AutoBackfillEnabled` and `ChannelMonthlyReports:AutoPublishTrendEnabled` are independent global primary API booleans, each defaulting to `false`.

| Backfill | Publish | Behavior |
|---|---|---|
| Off | Off | Skip; no backfill, query, billing, conversation, or send. |
| On | Off | Backfill and readiness only; no query, billing, conversation, or send. |
| On | On | Backfill → readiness → existing trend query → same-channel publish. |
| Off | On | Safe skip; no stale-data query or send, including stored-response replay. |

## 6. Acceptance Criteria

AC1. An original `channel_post` with `chat.type == "channel"`, a signed nonzero chat ID, positive message ID, and supported text is accepted for recognition without requiring `From`.

AC2. A supported textual media caption is recognized with the same rules as text.

AC3. Edited channel posts, unsupported media-only posts, and irrelevant posts produce no ingestion, query, billing, conversation, or Telegram send.

AC4. The originating signed chat ID and message ID are preserved through the backend and the chat ID is the only publication destination.

AC5. `فعالیت_ماهانه` and the approved explicit monthly production/sales report marker are recognized only with a one-month reporting-period marker; multi-month-only reports are rejected.

AC6. Persian and Arabic-Indic digits, Arabic/Persian letter variants, whitespace, and ZWNJ variants are normalized using bounded deterministic rules.

AC7. A valid Shamsi year is extracted and validated in the existing 1404–1500 range without inferring it from the current date.

AC8. A valid Shamsi month name or numeric month is extracted and validated in the existing 1–12 range.

AC9. Conflicting period markers, invalid dates, fiscal-year-end dates, missing fields, or multiple distinct months are rejected without ingestion.

AC10. Hashtag candidates resolve to exactly one eligible company through canonical exact ticker matching; unknown, ambiguous, or inconsistent mappings are rejected without guessing.

AC11. Both options bind as global primary API booleans and each omitted option defaults to `false`; malformed boolean configuration fails startup validation.

AC12. After authentication, recognition/resolution, and safe claim, the Feature 133 backend handler reads both bound options; with backfill off and publish off it records a terminal skip and performs no backfill, readiness query, billing, conversation, rendering, or send.

AC13. With backfill on and publish off, the Feature 133 backend handler runs the existing direct single-company/month ingestion and readiness check, then terminates successfully before trend query, AI billing reservation/finalization, conversation creation, rendering, or Telegram send.

AC14. With backfill on and publish on, the Feature 133 backend handler proceeds in this order: direct refresh, readiness, existing trend query, billing/conversation through the existing path, validation/rendering, and same-channel publication.

AC15. With backfill off and publish on, the Feature 133 backend handler records `AutoPublishRequiresBackfill` and skips backfill, use of existing/stored financial data, readiness, query, billing, conversation, rendering, publication, and stored-response replay.

AC16. Direct refresh reuses `ISingleCompanyMonthlyIngestionService.ExecuteDirectAsync` for the resolved company and requested Shamsi period; no duplicate financial ingestion or calculation engine is introduced.

AC17. A failed, `NoDataYet`, exception, or nonzero-error refresh suppresses trend query and publication.

AC18. Readiness reads the existing trend projection and requires expected company, requested year/month, snapshot existence, and `CalculatedAtUtc >= refreshStartUtc`.

AC19. A missing, mismatched, or pre-refresh snapshot is rejected even when the ingestion service reports completion.

AC20. Readiness does not require or imply that `SourceReportId` is exposed by the trend read projection.

AC21. The existing production/sales trend capability is invoked with the exact constructed query `روند تولید و فروش {canonicalSymbol}` through the DI-selected orchestration path.

AC22. The narrow routing correction prevents the canonical trend phrase from being misclassified as product comparison while explicit Feature 129 product-comparison requests retain their existing route.

AC23. A trend result is publishable only when it has no clarification/error and its company and requested period match the triggering post.

AC24. Publication-mode execution creates an isolated persisted conversation for the post and never reuses a linked subscriber's conversation or identity.

AC25. The unchanged `TelegramGateway:PrimaryApiKey`, gateway client, and existing/default tenant are used for both Feature 130 linking and Feature 133 transport; no second Feature 133 credential is introduced.

AC26. A valid existing-user-tenant Feature 130 link challenge succeeds through unchanged confirmation validation with automation disabled or enabled, while a mismatched tenant remains rejected.

AC27. Feature 133 uses the authenticated gateway API-client identity and tenant as authoritative, with no linked Telegram user identity or caller-selected execution actor.

AC28. Tenant, organization, billing-account, and actor identity values supplied in request payloads or Telegram text cannot override authenticated claims or billing selection.

AC29. Existing `BillableAccountResolver` organization resolution selects the `CustomerAccounts` organization for the authenticated tenant, and existing reservation/finalization charges that account; insufficient capacity prevents publication.

AC30. Backfill-only execution performs no AI billing or conversation creation; publication mode uses the existing billed query path.

AC31. Repeated delivery of the same post uses the approved durable post key and does not repeat successfully completed refresh/query work; confirmed outbound parts are skipped on replay, terminal skipped/backfill-only outcomes remain terminal after option changes, first-time posts older than 30 days are rejected, records are retained for 90 days, and expired in-flight work transitions to review-required without re-execution.

AC32. Parsing, resolution, refresh, readiness, query, billing, or send failures publish no misleading financial response; the handler enforces a bounded processing deadline below the primary request budget, state-file corruption or unavailability fails closed for the channel branch, send retries remain bounded, and ambiguous distributed exactly-once behavior is not claimed.

AC33. Deployment retains Feature 130 credential/link compatibility, keeps both automation defaults off, and does not enable production publication until the selected organization account is provisioned and funded.

## 7. Failure and Suppression Behavior

Relevant-invalid posts receive a durable terminal reason and no public financial error message. Refresh or readiness failures never authorize use of an old snapshot. Query or billing failures preserve the appropriate checkpoint but publish nothing. Telegram transient failures retry only unsent parts within the approved bound; permanent rejection or exhaustion records incomplete delivery and advances polling. Disabled outcomes are terminal control outcomes and are not retried or re-executed solely because configuration later changes.

## 8. Security and Billing Constraints

The backend accepts only the configured API client, unchanged tenant, authorized path, and allowed channel. Identity is derived from authenticated claims, never event content. The existing organization-account resolver owns billing; no new wallet, free system identity, tenant selector, billing subsystem, or admin-token distribution is allowed. Publication remains disabled operationally until account funding is confirmed.

## 9. Compatibility Requirements

Feature 130 ordinary messages, callbacks, account linking, offsets, multipart rendering, and admin backfill authorization remain unchanged. Feature 133 adds a channel-post branch to the existing gateway and assistant boundary, preserving same-channel destination and existing renderer behavior. V1, V2, semantic trend routing, and explicit product-comparison behavior receive regression coverage.

## 10. Non-Goals

No new bot, gateway service, database, broker, financial calculation engine, generic semantic router, billing or identity subsystem, tenant-selection API, workflow engine, per-channel configuration framework, OCR, LLM parser, channel administration UI, report correction workflow, or historical revision matching.

## 11. Traceability

The story is derived from approved design sections 4–18, approved review F1/F2 resolutions, and the three implementation slices in design section 22. Every criterion is mapped to one or more implementation tasks in `Tasks.md`.

## Review Corrections

F1 â€” Required trigger-age, retention, timeout, and fail-closed state protections have no concrete implementation owner â€” RESOLVED. AC31â€“AC32 now make the approved age, retention, deadline, review-required, and fail-closed behaviors observable.

F2 â€” Runtime ownership of the independent four-state gates is underspecified â€” RESOLVED. AC12â€“AC15 explicitly assign gate evaluation and ordered side-effect suppression to the Feature 133 backend handler.

STORY_READY_FOR_REVIEW
