# Feature 155 — Design

Status: Proposed — not approved

This file is the Gate A design proposal for Feature 155 (`specs/FEATURE_DEVELOPMENT_WORKFLOW.md`; decision OQ-003, 2026-11-09). It was written before the workflow was adopted and has not been reviewed yet.

## 1. Bounded context and language

Context: **AI Assistant / Dialogue Analytics** (read side of Conversations). Terms used consistently in code, API, and UI:

| Term | Meaning |
|---|---|
| Question turn | One user message plus the assistant message persisted by the same exchange (same `ConversationId` and identical `CreatedAt`, written by `ConversationRepository.AppendExchange`). |
| Channel | `customer`, `internal`, or `automation` (see §3). |
| Coverage bucket | Outcome class of a turn from the customer's point of view (see §4). |
| Gap turn | A turn whose bucket is not `answered`. |
| Demand theme | Roadmap grouping of a gap turn (T01–T15, T99; see §5). |
| Classifier version | Version of the bucket and theme rules. v1 = this document plus `queries.sql`. |

## 2. Data source (no new persistence in phase 1)

- `"Messages"`: `Role` (`User`/`Assistant`), `Content`, `CreatedAt`, and `AssistantPayloadJson` (serialized `AssistantMessagePayload`).
- `"Conversations"`: `ActorId`, `TenantId`.
- `auth_user_roles` / `auth_roles`: internal-user detection.

Payload fields read: `Outcome`, `OutcomeReasonCode`, `Intent`, `SemanticCapabilityCode`, `ClarificationRequired`. Payloads written before the outcome policy existed (observed until 2026-08-03) have no `Outcome`. They use the legacy text rules in §4.

## 3. Channels

1. `automation`: the conversation's `ActorId` is one of the configured API-key client IDs (`Authentication:ApiKeys:Clients[*].ClientId`, today the Telegram gateway). Its turns are bot-generated monthly-sales posts, one conversation per post, and not organic demand. The implementation reads the IDs from configuration; `queries.sql` hard-codes the current one only as a reference.
2. `internal`: the actor has any role whose name contains `admin`.
3. `customer`: everything else. **Only `customer` turns feed the ranking.**

## 4. Coverage buckets (classifier v1)

Outcome ordinals (`DialogueOutcome`): 0 Answered, 1 PartialAnswer, 2 ClarificationNeeded, 3 DisambiguationNeeded, 4 NoData, 5 Unsupported, 6 TemporarilyUnavailable, 7 Failed.

| Order | Condition | Bucket |
|---|---|---|
| 1 | Outcome 0/1 and the first 400 characters of the reply say the value is unavailable (`مقدار قابل اتکایی`, `قابل اجرا نیست`, `در دسترس نیست`, `یافت نشد`) | `soft_no_answer` |
| 2 | Outcome 0/1 and the question asks for something no capability provides (misroute lexicon in `queries.sql`: technical/chart, forecast/target, buy advice, valuation/equilibrium price, index, news, FX/funds, product-rate trends, chit-chat) | `misrouted` |
| 3 | Outcome 0/1 otherwise | `answered` |
| 4 | Outcome 5 | `unsupported` |
| 5 | Outcome 4, or reason `entity_not_found` | `no_data` |
| 6 | Outcome 2/3 | `clarification` |
| 7 | Outcome 6/7 | `failed` |
| 8 | Legacy: intent 15 (Unknown) or reply contains not-recognized/not-supported text | `unsupported` |
| 9 | Legacy: `ClarificationRequired` or a clarification prefix | `clarification` |
| 10 | Legacy: not-found text | `no_data` |
| 11 | Legacy: rules 1–3 | as above |

`misrouted` is a heuristic. It is reported separately as *hidden gap* so it never inflates explicit failures.

## 5. Demand themes (classifier v1, first match wins)

The order matters and is part of the version. The regex source of truth is `queries.sql`; labels are in Persian for the UI.

| Code | Theme | Roadmap lane |
|---|---|---|
| T15 | گفتگوی عمومی / معرفی دستیار | Onboarding copy / help answer |
| T04 | توصیه خرید/فروش و پیشنهاد سهم | Policy decision (advice boundaries) |
| T05 | پیش‌بینی سود و هدف قیمتی | New capability or explicit refusal copy |
| T02 | تحلیل تکنیکال، چارت قیمت و تابلو | New capability (price/market data) |
| T11 | شاخص و وضعیت کل بازار | New capability |
| T03 | ارزش‌گذاری، P/E و قیمت/گیج تعادلی | Re-enable retired or unavailable capability / data |
| T10 | غربالگری و رتبه‌بندی کل بازار | Feature 141 / scanner coverage |
| T12 | اخبار، مجامع، ارز و دارایی‌های خاص | New data source |
| T14 | روند نرخ/قیمت محصولات | Monthly data extension |
| T13 | مقایسه چند نماد | Re-enable retired capability |
| T09 | معیارهای مالی چنددوره‌ای و سفارشی | Feature 141 / statement coverage |
| T01 | تحلیل جامع / بررسی کلی سهم | Re-enable comprehensive analysis for the active provider |
| T07 | پوشش داده فروش و تولید ماهانه | Data ingestion coverage / entity resolution |
| T06 | نماد تنها بدون درخواست مشخص (≤ 3 words) | UX: default symbol snapshot instead of a refusal |
| T99 | سایر | Manual review |

Ranking: distinct customer users DESC, then turns DESC. Users matter more than turns because the baseline showed the same question repeated many times by a few users.

## 6. Architecture (clean architecture, Feature 149 pattern)

```
API  AdminAnalyticsController
     GET /api/v1/admin/analytics/unanswered-questions?from&to            (summary: Q1–Q4, Q6)
     GET /api/v1/admin/analytics/unanswered-questions/examples?from&to&theme   (Q5, audited)
     GET /api/v1/admin/analytics/unanswered-questions/export.csv?from&to
       │  policy admin.users.read; tenant from ICurrentActorContext
Application  Administration/UnansweredQuestions/
     GetUnansweredQuestionDemandReport (use case: validates window ≤ 180 days, orchestrates)
     QuestionCoveragePolicy        (pure: outcome/reason/intent/answer head → bucket)
     DemandThemeTaxonomy           (pure, versioned: question → theme; normalizes ي/ك)
     ChannelPolicy                 (pure: actor facts + automation IDs → channel)
     IUnansweredQuestionTurnSource (port: streams raw turn facts for a window)
Infrastructure  Administration/EfCoreUnansweredQuestionTurnSource
     raw SQL = the turns/facts CTEs of queries.sql (no classification in SQL),
     AsNoTracking, statement timeout 30 s, window filter on Messages.CreatedAt
Frontend  components/admin/unanswered-questions-panel.tsx (+ model.ts), tab in admin analytics
```

- Classification lives in the Application layer as pure functions, unit-tested without a database. SQL returns facts only, so the rules have one owner. `queries.sql` remains the manual and parity reference.
- Automation actor IDs come from configuration through an Application-owned options interface, never hard-coded in C#.
- In phase 1 aggregation happens in memory after classification. At about 500 customer turns per week this is small. Guard rails: window ≤ 180 days and a hard cap of 50 000 turns per request. The response reports `truncated: true` beyond the cap.

## 7. Privacy and audit

- The summary endpoint returns counts only.
- The examples endpoint returns question text (truncated to 200 characters, never the answer) and writes an `auth_security_admin_audits` record before responding. The record has action code `unanswered_questions.examples.read`, target type `dialogue_demand_theme`, target ID = theme code, and no question text. The read fails closed if the audit write fails.
- No actor IDs, emails, or conversation IDs in any response.
- Baseline reports with question text must not be committed to this public repository.

## 8. Performance and resilience

- Missing index: filtering by `Messages."CreatedAt"` alone cannot use the existing `(ConversationId, CreatedAt)` index. Add `IX_Messages_Role_CreatedAt` (`Role`, `CreatedAt`), created concurrently in its own migration.
- JSON parsing of `AssistantPayloadJson` per turn is acceptable at current volume. Phase 2 (when weekly customer turns exceed ~20 000, or the report p95 exceeds 3 s) adds a write-time projection. That projection is a `dialogue_turn_outcomes` table filled by `AppendExchange`, holding `Outcome`, reason, intent, capability, and channel as typed columns, plus a one-off backfill.
- Statement timeout and cancellation are propagated. The UI shows a retryable error; there are no automatic retries.

## 9. Risks found during the baseline

1. **Enum ordinals in persisted JSON.** `DialogueOutcome` and `DetectedIntent` are stored as integers. Reordering or inserting enum members silently re-labels history. Mitigation (task 155-T02): pin ordinals with a unit test, or switch the payload to `JsonStringEnumConverter` with a read-compatible converter.
2. **Misroute heuristic drift.** The lexicon is Persian keyword-based. The examples view lets an admin spot false positives; lexicon changes bump the classifier version.
3. **Suggestion-chip inflation.** Prompts clicked from suggestion chips repeat verbatim across users. Distinct-user ranking limits the effect; a later version may tag turns that came from chips.

## 10. Issues observed in the baseline (to triage separately, not part of this feature)

- Price replies are prefixed `واحد: میلیون ریال` although the value is a per-share price in rials.
- `قیمت تعادلی X`, `X ارزش خرید داره؟`, and `X در این قیمت خوبه؟` are answered with the last traded price.
- `تکنیکال X` is answered with product revenue mix; `پیوت X` with monthly sales; `ارزش ذاتی X` with the monthly sales trend.
- Common words are resolved as symbols (`آینده‌نگر` → `آینده`), and a follow-up `فسوژ هم نشون بده` returned `دسبحان`.
- Large, well-known symbols (`فملی`, `فولاد`) return `entity_ambiguous` for monthly sales.
- P/E questions return "no reliable value" for large caps.
