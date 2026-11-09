-- Feature 155 — Unanswered Question Demand Report: reference queries (classifier v1).
--
-- Read-only. Run inside a read-only session, e.g.
--   PGOPTIONS='-c default_transaction_read_only=on -c statement_timeout=60000' \
--     psql "$DSN" -v ON_ERROR_STOP=1 -f queries.sql
-- Never commit connection strings or passwords; supply the DSN from the environment.
--
-- Q0 defines a session-scoped TEMP VIEW (allowed in read-only transactions) holding one row per
-- question turn with its channel, coverage bucket and demand theme. Q1–Q6 read from it.
-- Edit the window and the automation actor list in the `params` CTE only.
--
-- Classifier v1 is the single source of these rules until the Application-layer
-- `QuestionCoveragePolicy` / `DemandThemeTaxonomy` (see design.md) replace it. When a rule
-- changes, bump the version in this header and in design.md together.

-- ---------------------------------------------------------------------------------------------
-- Q0. Classified question turns
-- ---------------------------------------------------------------------------------------------
CREATE TEMP VIEW unanswered_demand_turns AS
WITH params AS (
    SELECT now() - interval '180 days'                                     AS from_ts,
           now()                                                           AS to_ts,
           -- API-key clients that post automated, non-organic questions (Telegram gateway batch).
           -- Source of truth: Authentication:ApiKeys:Clients in configuration.
           ARRAY['22222222-2222-2222-2222-222222222222']::uuid[]           AS automation_actor_ids
),
turns AS (
    -- A turn = the user message and the assistant message persisted by the same exchange
    -- (same ConversationId and identical CreatedAt; see ConversationRepositories.AppendExchange).
    SELECT u."Id"                                   AS user_message_id,
           u."CreatedAt"                            AS created_at,
           c."TenantId"                             AS tenant_id,
           c."ActorId"                              AS actor_id,
           u."Content"                              AS question,
           translate(u."Content", 'يك', 'یک')       AS q,          -- Arabic → Persian letters
           left(a."Content", 400)                   AS answer_head,
           a."AssistantPayloadJson"::jsonb          AS p
    FROM "Messages" u
    JOIN "Messages" a ON a."ConversationId" = u."ConversationId"
                     AND a."CreatedAt" = u."CreatedAt"
                     AND a."Role" = 'Assistant'
    JOIN "Conversations" c ON c."Id" = u."ConversationId"
    CROSS JOIN params
    WHERE u."Role" = 'User'
      AND u."CreatedAt" >= params.from_ts AND u."CreatedAt" < params.to_ts
),
facts AS (
    SELECT t.*,
           CASE
             WHEN t.actor_id = ANY (params.automation_actor_ids) THEN 'automation'
             WHEN EXISTS (SELECT 1 FROM auth_user_roles ur JOIN auth_roles r ON r."Id" = ur."RoleId"
                          WHERE ur."UserId" = t.actor_id AND r."Name" ILIKE '%admin%') THEN 'internal'
             ELSE 'customer'
           END                                        AS channel,
           -- DialogueOutcome and DetectedIntent are serialized as enum ORDINALS (see design.md, Risks):
           -- Outcome 0 Answered, 1 PartialAnswer, 2 ClarificationNeeded, 3 DisambiguationNeeded,
           --         4 NoData, 5 Unsupported, 6 TemporarilyUnavailable, 7 Failed; NULL = legacy payload.
           -- Intent 15 = Unknown.
           (t.p ->> 'Outcome')::int                   AS outcome,
           NULLIF(t.p ->> 'OutcomeReasonCode', '')    AS reason,
           (t.p ->> 'Intent')::int                    AS intent,
           t.p ->> 'SemanticCapabilityCode'           AS capability,
           COALESCE((t.p ->> 'ClarificationRequired')::boolean, false) AS clarification_required,
           -- "Answered" text that actually says the value is unavailable.
           t.answer_head ~ '(مقدار قابل اتکایی|قابل اجرا نیست|در دسترس نیست|یافت نشد)' AS soft_no_answer,
           -- "Answered" turns whose wording asks for something no current capability provides.
           t.q ~* ('(^(سلام|hi|hello)\y|معرفی کن|چیکار می ?کنی|بیسواد|پس چی رو بپرسم|اکانت من'
                   '|تکنیکال|چارت|مقاومت|حمایت|پول هوشمند|صف (خرید|فروش)|حجم معاملات|نمودار (?!روند)'
                   '|هدف قیمتی|پیش ?بینی|ماه آینده|سال آینده|یکسال آینده|روند فروش آینده'
                   '|بخرم|چی بخرم|برای خرید|ارزش سرمایه ?گذاری|میانگین کم|کم ?ریسک|خوبه برای|ارزش ذاتی|ارزش ?گذاری|ارزیابی'
                   '|شاخص|آزاد شناور|اخبار|خبر|اطلاعیه|کدال|افشا'
                   '|ارز ترجیحی|گواهی سپرده|فوروارد|صندوق|شمش|نرخ محصولات|تغییرات نرخ'
                   '|قیمت تعادلی|پیوت|ترکیب دارایی|روند خرید|پیشنهاد میدی|نمودار قیمتی|ارزش خرید|در این قیمت)') AS misroute_hint
    FROM turns t CROSS JOIN params
),
bucketed AS (
    SELECT f.*,
           CASE
             WHEN f.outcome IN (0, 1) THEN
               CASE WHEN f.soft_no_answer THEN 'soft_no_answer'
                    WHEN f.misroute_hint  THEN 'misrouted'
                    ELSE 'answered' END
             WHEN f.outcome = 5 THEN 'unsupported'
             WHEN f.outcome = 4 OR f.reason = 'entity_not_found' THEN 'no_data'
             WHEN f.outcome IN (2, 3) THEN 'clarification'
             WHEN f.outcome IS NOT NULL THEN 'failed'
             -- Legacy payloads (before Outcome existed): infer from intent and reply text.
             WHEN f.intent = 15
               OR f.answer_head ~* '(not recognized|not supported|پشتیبانی نمی|خارج از (حوزه|محدوده))' THEN 'unsupported'
             WHEN f.clarification_required
               OR f.answer_head ~* '(^Clarification needed|منظورتان)' THEN 'clarification'
             WHEN f.answer_head ~* '(یافت نشد|موجود نیست|no data|not found)' THEN 'no_data'
             WHEN f.soft_no_answer THEN 'soft_no_answer'
             WHEN f.misroute_hint  THEN 'misrouted'
             ELSE 'answered'
           END AS bucket
    FROM facts f
)
SELECT b.user_message_id, b.created_at, b.tenant_id, b.actor_id, b.channel, b.question,
       b.outcome, b.reason, b.intent, b.capability, b.bucket,
       b.bucket <> 'answered' AS is_gap,
       CASE  -- first match wins; the order is part of classifier v1
         WHEN b.q ~* '(^(سلام|hi|hello)|معرفی کن و بگو|بیسواد|پس چی رو بپرسم|اکانت من|^hohfv$)'                       THEN 'T15'
         WHEN b.q ~* '(بخرم|چی بخرم|ارزش خرید|ارزش سرمایه ?گذاری|خوبه برای خرید|برای خرید خوب|مناسب خرید|در این قیمت خوبه|میانگین کم|کم ?ریسک|پیشنهاد میدی)' THEN 'T04'
         WHEN b.q ~* '(هدف قیمتی|پیش ?بینی|ماه آینده|روند فروش آینده)'                                                 THEN 'T05'
         WHEN b.q ~* '(تکنیکال|چارت|مقاومت|مقامت|^نمودار|حمایت|پیوت|پول هوشمند|صف (خرید|فروش)|نمودار قیمتی|نمودار .*رو بررسی|روند خرید|حجم معاملات)' THEN 'T02'
         WHEN b.q ~* '(شاخص|آزاد شناور)'                                                                                 THEN 'T11'
         WHEN b.q ~* '(p/?e|\ype\y|پی بر ای|قیمت تعادلی|گیج|ارزش ذاتی|ارزش بازار|مارکت ولیو)'                            THEN 'T03'
         WHEN b.q ~* '(پیدا کن|فهرست|لیست کن|بیشترین|کمترین|بالاترین|پایین ?ترین|ده سهم|ده شرکت|سهمی که|سهمامی که|نمادهای با|شرکتهایی که|کدام سهام|سهام با)' THEN 'T10'
         WHEN b.q ~* '(اخبار|خبر|ارز ترجیحی|گواهی سپرده|سود مجمع|ترکیب دارایی|دارایی های|شمش)'                          THEN 'T12'
         WHEN b.q ~* '(نرخ محصولات|تغییرات نرخ)'                                                                         THEN 'T14'
         WHEN b.q ~* '(مقایسه کن|با هم)'                                                                                 THEN 'T13'
         WHEN b.q ~* '(حاشیه سود|سود .*(امسال|۱۴۰۵|1405)|سود نماد|۷ ماهه|7 ماهه|صادراتی|سودسازی|سود سازی|سود خالص|سود اوری|صورت|ترازنامه|درامد|درآمد|گزارشات فصلی|گزارش عملکرد)' THEN 'T09'
         WHEN b.q ~* '(بررسی|ببرسی|برسی|تحلیل|ارزیابی|در مورد|هر چی میدونی|همه چیز|چجوریه|گزارش کلی|چیزی راجب|عملکردش)'   THEN 'T01'
         WHEN b.q ~* '(روند فروش|فروش ماهانه|تولید|ترکیب فروش|ترکیب محصولات|محصولات|فروش)'                              THEN 'T07'
         WHEN coalesce(array_length(regexp_split_to_array(btrim(b.q), '\s+'), 1), 0) <= 3                                THEN 'T06'
         ELSE 'T99'
       END AS theme
FROM bucketed b;

-- Theme labels (Persian, for presentation):
--   T01 تحلیل جامع / بررسی کلی سهم            T09 معیارهای مالی چنددوره‌ای و سفارشی
--   T02 تحلیل تکنیکال، چارت قیمت و تابلو       T10 غربالگری و رتبه‌بندی کل بازار
--   T03 ارزش‌گذاری، P/E و قیمت/گیج تعادلی     T11 شاخص و وضعیت کل بازار
--   T04 توصیه خرید/فروش و پیشنهاد سهم         T12 اخبار، مجامع، ارز و دارایی‌های خاص
--   T05 پیش‌بینی سود و هدف قیمتی               T13 مقایسه چند نماد
--   T06 نماد تنها بدون درخواست مشخص            T14 روند نرخ/قیمت محصولات
--   T07 پوشش داده فروش و تولید ماهانه          T15 گفتگوی عمومی / معرفی دستیار
--   T99 سایر

-- ---------------------------------------------------------------------------------------------
-- Q1. Headline per channel: turns, distinct users, gap rate
-- ---------------------------------------------------------------------------------------------
SELECT channel,
       count(*)                                         AS turns,
       count(DISTINCT actor_id)                         AS users,
       count(*) FILTER (WHERE is_gap)                   AS gap_turns,
       round(100.0 * count(*) FILTER (WHERE is_gap) / NULLIF(count(*), 0), 1) AS gap_rate_pct
FROM unanswered_demand_turns
GROUP BY channel
ORDER BY turns DESC;

-- ---------------------------------------------------------------------------------------------
-- Q2. Customer coverage buckets
-- ---------------------------------------------------------------------------------------------
SELECT bucket, count(*) AS turns, count(DISTINCT actor_id) AS users
FROM unanswered_demand_turns
WHERE channel = 'customer'
GROUP BY bucket
ORDER BY turns DESC;

-- ---------------------------------------------------------------------------------------------
-- Q3. Roadmap ranking: customer gap demand per theme (rank by distinct users, then turns)
-- ---------------------------------------------------------------------------------------------
SELECT theme,
       count(DISTINCT actor_id)                                   AS users,
       count(*)                                                   AS turns,
       count(*) FILTER (WHERE bucket = 'unsupported')             AS unsupported,
       count(*) FILTER (WHERE bucket = 'no_data')                 AS no_data,
       count(*) FILTER (WHERE bucket = 'clarification')           AS clarification,
       count(*) FILTER (WHERE bucket IN ('misrouted', 'soft_no_answer')) AS hidden,
       count(*) FILTER (WHERE bucket = 'failed')                  AS failed,
       max(created_at)::date                                      AS last_seen
FROM unanswered_demand_turns
WHERE channel = 'customer' AND is_gap
GROUP BY theme
ORDER BY users DESC, turns DESC;

-- ---------------------------------------------------------------------------------------------
-- Q4. Root cause: which outcome reason / capability produces customer gaps
-- ---------------------------------------------------------------------------------------------
SELECT bucket, coalesce(reason, '-') AS reason, coalesce(capability, '-') AS capability,
       count(*) AS turns, count(DISTINCT actor_id) AS users
FROM unanswered_demand_turns
WHERE channel = 'customer' AND is_gap
GROUP BY 1, 2, 3
ORDER BY turns DESC;

-- ---------------------------------------------------------------------------------------------
-- Q5. Example questions per theme (deduplicated, most frequent first, 10 per theme)
-- ---------------------------------------------------------------------------------------------
SELECT theme, question, asked, users, buckets
FROM (
    SELECT theme,
           regexp_replace(btrim(question), '\s+', ' ', 'g')        AS question,
           count(*)                                                AS asked,
           count(DISTINCT actor_id)                                AS users,
           string_agg(DISTINCT bucket, ',')                        AS buckets,
           row_number() OVER (PARTITION BY theme ORDER BY count(*) DESC, max(created_at) DESC) AS rn
    FROM unanswered_demand_turns
    WHERE channel = 'customer' AND is_gap
    GROUP BY theme, regexp_replace(btrim(question), '\s+', ' ', 'g')
) x
WHERE rn <= 10
ORDER BY theme, asked DESC;

-- ---------------------------------------------------------------------------------------------
-- Q6. Trend: customer gap rate by week
-- ---------------------------------------------------------------------------------------------
SELECT date_trunc('week', created_at)::date AS week,
       count(*) AS turns, count(DISTINCT actor_id) AS users,
       count(*) FILTER (WHERE is_gap) AS gap_turns,
       round(100.0 * count(*) FILTER (WHERE is_gap) / NULLIF(count(*), 0), 1) AS gap_rate_pct
FROM unanswered_demand_turns
WHERE channel = 'customer'
GROUP BY 1
ORDER BY 1;
