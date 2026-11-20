-- Feature 164, task 7.1: deterministic synthetic dataset for measuring the admin conversation-log queries.
-- Run against a THROWAWAY database after schema-from-migrations.sql. Never run against a real database.
--
--   docker exec -i <container> psql -U postgres -d measure -v ON_ERROR_STOP=1 < schema-from-migrations.sql
--   docker exec -i <container> psql -U postgres -d measure -v ON_ERROR_STOP=1 < seed-synthetic.sql
--
-- Shape (anchored at 2026-11-20 12:00 UTC so the plans are reproducible):
--   * 5,000 users; users 4,801–5,000 belong to a second tenant (about 4% of conversations).
--   * 50,000 conversations started over the last 180 days; user choice skewed (a few heavy users).
--   * 1–9 exchanges per conversation (about 5 on average). An exchange stores the User row and the Assistant row with
--     the SAME CreatedAt, as the chat does; ids are md5-derived, so the answer id sorts before the question id about half
--     the time (BUG-037 realism). About 5% of turns have no answer, about 3% have a second Assistant row 1 s later.
--   * Assistant payloads: 10% NULL (legacy), 0.5% malformed JSON, the rest valid JSON with an integer "Outcome"
--     (80% Answered = 0, the rest spread over 1–7). Payload size: 70% ~1.5 KB, 25% ~6 KB, 5% ~20 KB.
--   * Persian question text of 40–200 characters; answers of 300–4,000 characters.
-- setseed makes random() deterministic; ids come from md5 so they are deterministic too.

SELECT setseed(0.164);

CREATE TEMP TABLE seed_conversations AS
SELECT j,
       md5('conv-' || j)::uuid AS id,
       u AS user_no,
       md5('user-' || u)::uuid AS actor_id,
       CASE WHEN u > 4800 THEN '22222222-2222-2222-2222-222222222222'::uuid
            ELSE '11111111-1111-1111-1111-111111111111'::uuid END AS tenant_id,
       timestamptz '2026-11-20 12:00:00+00' - random() * interval '180 days' AS started_at,
       1 + floor(random() * 9)::int AS exchanges,
       random() AS title_roll
FROM (SELECT j, 1 + floor(power(random(), 2) * 5000)::int AS u FROM generate_series(1, 50000) AS j) AS s;

CREATE TEMP TABLE seed_exchanges AS
SELECT c.j,
       c.id AS conversation_id,
       k,
       c.started_at + (k - 1) * interval '3 minutes' + random() * interval '2 minutes' AS at,
       random() AS answer_roll,
       random() AS payload_roll,
       random() AS outcome_roll,
       random() AS size_roll,
       1 + floor(random() * 12)::int AS question_template,
       1 + floor(random() * 6)::int AS answer_repeat,
       random() AS continuation_roll
FROM seed_conversations AS c
CROSS JOIN LATERAL generate_series(1, c.exchanges) AS k;

INSERT INTO "Conversations" ("Id", "TenantId", "ActorId", "StartedAt", "UpdatedAt", "MessageCount", "Title")
SELECT c.id, c.tenant_id, c.actor_id, c.started_at,
       c.started_at + c.exchanges * interval '3 minutes',
       c.exchanges * 2,
       CASE WHEN c.title_roll < 0.3 THEN 'New conversation' ELSE 'تحلیل نماد شماره ' || c.j END
FROM seed_conversations AS c;

-- Questions.
INSERT INTO "Messages" ("Id", "ConversationId", "Role", "Content", "CreatedAt")
SELECT md5('msg-' || e.j || '-' || e.k || '-u')::uuid,
       e.conversation_id,
       'User',
       (ARRAY[
           'سود خالص فولاد در فصل گذشته چقدر بوده است؟',
           'فروش ماهانه خودرو را با ماه قبل مقایسه کن',
           'نمادهایی که P/E کمتر از پنج دارند را نشان بده',
           'ترازنامه شرکت پتروشیمی را تحلیل کن',
           'جریان نقد عملیاتی شستا در سال مالی اخیر',
           'كدام شركت‌ها رشد فروش بیش از ده درصد داشته‌اند؟',
           'قیمت تعادلی سهم وبملت چقدر است؟',
           'گزارش فعالیت ماهانه شپنا را خلاصه کن',
           'سهامداران عمده فارس چه کسانی هستند؟',
           'بازده سرمایه شرکت‌های سیمانی را رتبه‌بندی کن',
           'آیا سود تقسیمی خساپا افزایش یافته است؟',
           'روند تولید و فروش محصولات کگل در شش ماه گذشته'
       ])[e.question_template]
       || CASE WHEN e.size_roll < 0.3 THEN ' لطفاً با جزئیات و جدول مقایسه‌ای و توضیح دلایل تغییرات ارائه کن.' ELSE '' END
       || ' (' || e.k || ')',
       e.at
FROM seed_exchanges AS e;

-- Answers: same CreatedAt as the question.
INSERT INTO "Messages" ("Id", "ConversationId", "Role", "Content", "AssistantPayloadJson", "CreatedAt")
SELECT md5('msg-' || e.j || '-' || e.k || '-a')::uuid,
       e.conversation_id,
       'Assistant',
       repeat('بر اساس آخرین صورت‌های مالی منتشرشده، سود خالص شرکت نسبت به دوره مشابه سال قبل تغییر کرده است. ', e.answer_repeat * 5),
       CASE
           WHEN e.payload_roll < 0.10 THEN NULL
           WHEN e.payload_roll < 0.105 THEN '{"Version":2,"Outcome":'
           ELSE '{"Version":2,"Intent":"FinancialData","ClarificationRequired":false,"TextAnswer":"'
                || repeat('متن پاسخ ساختاریافته با جدول داده‌ها ', CASE WHEN e.size_roll < 0.70 THEN 40 WHEN e.size_roll < 0.95 THEN 160 ELSE 540 END)
                || '","Outcome":'
                || CASE WHEN e.outcome_roll < 0.80 THEN 0 ELSE 1 + floor((e.outcome_roll - 0.80) / 0.20 * 7)::int END
                || ',"OutcomeReasonCode":null}'
       END,
       e.at
FROM seed_exchanges AS e
WHERE e.answer_roll >= 0.05;

-- Continuations: a second Assistant row 1 s later in about 3% of answered turns.
INSERT INTO "Messages" ("Id", "ConversationId", "Role", "Content", "AssistantPayloadJson", "CreatedAt")
SELECT md5('msg-' || e.j || '-' || e.k || '-a2')::uuid,
       e.conversation_id,
       'Assistant',
       'ادامه پاسخ: جزئیات بیشتر درباره روند فصلی.',
       '{"Version":2,"Outcome":0}',
       e.at + interval '1 second'
FROM seed_exchanges AS e
WHERE e.answer_roll >= 0.05 AND e.continuation_roll < 0.03;

ANALYZE "Conversations";
ANALYZE "Messages";

SELECT (SELECT count(*) FROM "Conversations") AS conversations,
       (SELECT count(*) FROM "Messages") AS messages,
       (SELECT count(*) FROM "Messages" WHERE "Role" = 'User') AS questions,
       (SELECT count(DISTINCT "ActorId") FROM "Conversations") AS users_with_conversations,
       pg_size_pretty(pg_total_relation_size('"Messages"')) AS messages_size;
