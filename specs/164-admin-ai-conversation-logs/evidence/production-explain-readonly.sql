-- Feature 164, task 7.2: READ-ONLY plan check of the admin conversation-log list queries, for the owner to run on
-- production. The agent has no production access; send the output back so it can be recorded in evidence/.
--
-- What it does: for each scenario it opens a transaction, makes it READ ONLY, sets a 5 s statement timeout, runs
-- EXPLAIN (ANALYZE, BUFFERS) on one SELECT and rolls back. EXPLAIN ANALYZE really executes the SELECT (to time it);
-- nothing writes, and READ ONLY makes any write fail. Each statement is capped at 5 s (12 statements, so at most about
-- a minute). A scenario that hits the cap prints "canceling statement due to statement timeout" — that line is a
-- result too, please keep it.
--
-- How to run (psql 16 or later, against the database that holds "Messages" and "Conversations"):
--   1. Fill in the values marked <<FILL IN>> below (tenant id, three user ids that have conversations, a search
--      text). Keep the date range or move it to the last 30 days in Tehran time.
--   2. psql "<connection string>" -v ON_ERROR_STOP=0 -f production-explain-readonly.sql > f164-prod-explain.txt 2>&1
--   3. Send back f164-prod-explain.txt and, if possible, the result of (also read-only):
--        SELECT count(*) FROM "Messages";
--        SELECT pg_size_pretty(pg_total_relation_size('"Messages"'));
--        SHOW shared_buffers; SHOW work_mem;
--      The output holds plans and timings only, no message text.
--
-- <<FILL IN>> the tenant id that the admin panel uses:
\set tenant_id '00000000-0000-0000-0000-000000000000'
-- Last 30 Tehran days ending today, as UTC instants: [today-29 00:00 Tehran, tomorrow 00:00 Tehran).
-- Tehran is UTC+03:30, so 00:00 Tehran is 20:30Z of the previous day. <<FILL IN>> if you run it on another day:
\set from_utc '2026-10-21T20:30:00Z'
\set to_utc '2026-11-20T20:30:00Z'
-- <<FILL IN>> a short search text, for example a company name; keep the surrounding % signs:
\set text_pattern '%سود%'
-- <<FILL IN>> three user ids (Auth user id = Conversations."ActorId"), comma-separated inside the braces:
\set actor_ids '{00000000-0000-0000-0000-000000000001,00000000-0000-0000-0000-000000000002,00000000-0000-0000-0000-000000000003}'
\timing on
-- Fixed by the code (AdminConversationLogQueryRules): preview lengths.
\set question_preview_length 240
\set answer_preview_length 320
-- Failed = 7 (DialogueOutcome).
\set outcome 7
\pset pager off

-- [A-count] default: last 30 Tehran days, page 1, size 20
\echo [A-count]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT s."Value" FROM (
    SELECT count(*)::int AS "Value" FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId" WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
) AS s
LIMIT 2;
ROLLBACK;

-- [A-page] default: last 30 Tehran days, page 1, size 20
\echo [A-page]
\set take 20
\set skip 0
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT q."Id" AS "TurnId",
       q."ConversationId",
       c."Title" AS "ConversationTitle",
       c."ActorId" AS "UserId",
       left(q."Content", :'question_preview_length'::integer) AS "QuestionPreview",
       q."CreatedAt" AS "QuestionCreatedAt",
       a."AnswerId",
       a."AnswerPreview",
       a."AnswerCreatedAt",
       a."Outcome"
FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
LEFT JOIN LATERAL (
    SELECT n."CreatedAt"
    FROM "Messages" AS n
    WHERE n."ConversationId" = q."ConversationId"
      AND n."Role" = 'User'
      AND (n."CreatedAt", n."Id") > (q."CreatedAt", q."Id")
    ORDER BY n."CreatedAt", n."Id"
    LIMIT 1
) AS n ON TRUE
LEFT JOIN LATERAL (
    SELECT m."Id" AS "AnswerId",
           left(m."Content", :'answer_preview_length'::integer) AS "AnswerPreview",
           m."CreatedAt" AS "AnswerCreatedAt",
           CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb') THEN
               CASE WHEN jsonb_typeof(m."AssistantPayloadJson"::jsonb -> 'Outcome') = 'number'
                         AND (m."AssistantPayloadJson"::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                         AND length(m."AssistantPayloadJson"::jsonb ->> 'Outcome') <= 9
                    THEN (m."AssistantPayloadJson"::jsonb ->> 'Outcome')::int
               END
           END AS "Outcome"
    FROM "Messages" AS m
    WHERE m."ConversationId" = q."ConversationId"
      AND m."Role" = 'Assistant'
      AND m."CreatedAt" >= q."CreatedAt"
      AND (n."CreatedAt" IS NULL OR m."CreatedAt" < n."CreatedAt")
    ORDER BY m."CreatedAt", m."Id"
    LIMIT 1
) AS a ON TRUE
WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
ORDER BY q."CreatedAt" DESC, q."Id" DESC
LIMIT :'take'::integer OFFSET :'skip'::integer;
ROLLBACK;

-- [B-count] text search (q) in the 30-day window
\echo [B-count]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT s."Value" FROM (
    SELECT count(*)::int AS "Value" FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId" WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
  AND translate(q."Content", 'يىك', 'ییک') ILIKE :'text_pattern'::text ESCAPE '\'
) AS s
LIMIT 2;
ROLLBACK;

-- [B-page] text search (q) in the 30-day window
\echo [B-page]
\set take 20
\set skip 0
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT q."Id" AS "TurnId",
       q."ConversationId",
       c."Title" AS "ConversationTitle",
       c."ActorId" AS "UserId",
       left(q."Content", :'question_preview_length'::integer) AS "QuestionPreview",
       q."CreatedAt" AS "QuestionCreatedAt",
       a."AnswerId",
       a."AnswerPreview",
       a."AnswerCreatedAt",
       a."Outcome"
FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
LEFT JOIN LATERAL (
    SELECT n."CreatedAt"
    FROM "Messages" AS n
    WHERE n."ConversationId" = q."ConversationId"
      AND n."Role" = 'User'
      AND (n."CreatedAt", n."Id") > (q."CreatedAt", q."Id")
    ORDER BY n."CreatedAt", n."Id"
    LIMIT 1
) AS n ON TRUE
LEFT JOIN LATERAL (
    SELECT m."Id" AS "AnswerId",
           left(m."Content", :'answer_preview_length'::integer) AS "AnswerPreview",
           m."CreatedAt" AS "AnswerCreatedAt",
           CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb') THEN
               CASE WHEN jsonb_typeof(m."AssistantPayloadJson"::jsonb -> 'Outcome') = 'number'
                         AND (m."AssistantPayloadJson"::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                         AND length(m."AssistantPayloadJson"::jsonb ->> 'Outcome') <= 9
                    THEN (m."AssistantPayloadJson"::jsonb ->> 'Outcome')::int
               END
           END AS "Outcome"
    FROM "Messages" AS m
    WHERE m."ConversationId" = q."ConversationId"
      AND m."Role" = 'Assistant'
      AND m."CreatedAt" >= q."CreatedAt"
      AND (n."CreatedAt" IS NULL OR m."CreatedAt" < n."CreatedAt")
    ORDER BY m."CreatedAt", m."Id"
    LIMIT 1
) AS a ON TRUE
WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
  AND translate(q."Content", 'يىك', 'ییک') ILIKE :'text_pattern'::text ESCAPE '\'
ORDER BY q."CreatedAt" DESC, q."Id" DESC
LIMIT :'take'::integer OFFSET :'skip'::integer;
ROLLBACK;

-- [C1-count] status Failed in the 30-day window
\echo [C1-count]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT s."Value" FROM (
    SELECT count(*)::int AS "Value" FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
LEFT JOIN LATERAL (
    SELECT n."CreatedAt"
    FROM "Messages" AS n
    WHERE n."ConversationId" = q."ConversationId"
      AND n."Role" = 'User'
      AND (n."CreatedAt", n."Id") > (q."CreatedAt", q."Id")
    ORDER BY n."CreatedAt", n."Id"
    LIMIT 1
) AS n ON TRUE
LEFT JOIN LATERAL (
    SELECT m."Id" AS "AnswerId",
           left(m."Content", :'answer_preview_length'::integer) AS "AnswerPreview",
           m."CreatedAt" AS "AnswerCreatedAt",
           CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb') THEN
               CASE WHEN jsonb_typeof(m."AssistantPayloadJson"::jsonb -> 'Outcome') = 'number'
                         AND (m."AssistantPayloadJson"::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                         AND length(m."AssistantPayloadJson"::jsonb ->> 'Outcome') <= 9
                    THEN (m."AssistantPayloadJson"::jsonb ->> 'Outcome')::int
               END
           END AS "Outcome"
    FROM "Messages" AS m
    WHERE m."ConversationId" = q."ConversationId"
      AND m."Role" = 'Assistant'
      AND m."CreatedAt" >= q."CreatedAt"
      AND (n."CreatedAt" IS NULL OR m."CreatedAt" < n."CreatedAt")
    ORDER BY m."CreatedAt", m."Id"
    LIMIT 1
) AS a ON TRUE WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
  AND a."Outcome" = :'outcome'::integer
) AS s
LIMIT 2;
ROLLBACK;

-- [C1-page] status Failed in the 30-day window
\echo [C1-page]
\set take 20
\set skip 0
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT q."Id" AS "TurnId",
       q."ConversationId",
       c."Title" AS "ConversationTitle",
       c."ActorId" AS "UserId",
       left(q."Content", :'question_preview_length'::integer) AS "QuestionPreview",
       q."CreatedAt" AS "QuestionCreatedAt",
       a."AnswerId",
       a."AnswerPreview",
       a."AnswerCreatedAt",
       a."Outcome"
FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
LEFT JOIN LATERAL (
    SELECT n."CreatedAt"
    FROM "Messages" AS n
    WHERE n."ConversationId" = q."ConversationId"
      AND n."Role" = 'User'
      AND (n."CreatedAt", n."Id") > (q."CreatedAt", q."Id")
    ORDER BY n."CreatedAt", n."Id"
    LIMIT 1
) AS n ON TRUE
LEFT JOIN LATERAL (
    SELECT m."Id" AS "AnswerId",
           left(m."Content", :'answer_preview_length'::integer) AS "AnswerPreview",
           m."CreatedAt" AS "AnswerCreatedAt",
           CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb') THEN
               CASE WHEN jsonb_typeof(m."AssistantPayloadJson"::jsonb -> 'Outcome') = 'number'
                         AND (m."AssistantPayloadJson"::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                         AND length(m."AssistantPayloadJson"::jsonb ->> 'Outcome') <= 9
                    THEN (m."AssistantPayloadJson"::jsonb ->> 'Outcome')::int
               END
           END AS "Outcome"
    FROM "Messages" AS m
    WHERE m."ConversationId" = q."ConversationId"
      AND m."Role" = 'Assistant'
      AND m."CreatedAt" >= q."CreatedAt"
      AND (n."CreatedAt" IS NULL OR m."CreatedAt" < n."CreatedAt")
    ORDER BY m."CreatedAt", m."Id"
    LIMIT 1
) AS a ON TRUE
WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
  AND a."Outcome" = :'outcome'::integer
ORDER BY q."CreatedAt" DESC, q."Id" DESC
LIMIT :'take'::integer OFFSET :'skip'::integer;
ROLLBACK;

-- [C2-count] status NoResponse in the 30-day window
\echo [C2-count]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT s."Value" FROM (
    SELECT count(*)::int AS "Value" FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
LEFT JOIN LATERAL (
    SELECT n."CreatedAt"
    FROM "Messages" AS n
    WHERE n."ConversationId" = q."ConversationId"
      AND n."Role" = 'User'
      AND (n."CreatedAt", n."Id") > (q."CreatedAt", q."Id")
    ORDER BY n."CreatedAt", n."Id"
    LIMIT 1
) AS n ON TRUE
LEFT JOIN LATERAL (
    SELECT m."Id" AS "AnswerId",
           left(m."Content", :'answer_preview_length'::integer) AS "AnswerPreview",
           m."CreatedAt" AS "AnswerCreatedAt",
           CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb') THEN
               CASE WHEN jsonb_typeof(m."AssistantPayloadJson"::jsonb -> 'Outcome') = 'number'
                         AND (m."AssistantPayloadJson"::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                         AND length(m."AssistantPayloadJson"::jsonb ->> 'Outcome') <= 9
                    THEN (m."AssistantPayloadJson"::jsonb ->> 'Outcome')::int
               END
           END AS "Outcome"
    FROM "Messages" AS m
    WHERE m."ConversationId" = q."ConversationId"
      AND m."Role" = 'Assistant'
      AND m."CreatedAt" >= q."CreatedAt"
      AND (n."CreatedAt" IS NULL OR m."CreatedAt" < n."CreatedAt")
    ORDER BY m."CreatedAt", m."Id"
    LIMIT 1
) AS a ON TRUE WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
  AND a."AnswerId" IS NULL
) AS s
LIMIT 2;
ROLLBACK;

-- [C2-page] status NoResponse in the 30-day window
\echo [C2-page]
\set take 20
\set skip 0
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT q."Id" AS "TurnId",
       q."ConversationId",
       c."Title" AS "ConversationTitle",
       c."ActorId" AS "UserId",
       left(q."Content", :'question_preview_length'::integer) AS "QuestionPreview",
       q."CreatedAt" AS "QuestionCreatedAt",
       a."AnswerId",
       a."AnswerPreview",
       a."AnswerCreatedAt",
       a."Outcome"
FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
LEFT JOIN LATERAL (
    SELECT n."CreatedAt"
    FROM "Messages" AS n
    WHERE n."ConversationId" = q."ConversationId"
      AND n."Role" = 'User'
      AND (n."CreatedAt", n."Id") > (q."CreatedAt", q."Id")
    ORDER BY n."CreatedAt", n."Id"
    LIMIT 1
) AS n ON TRUE
LEFT JOIN LATERAL (
    SELECT m."Id" AS "AnswerId",
           left(m."Content", :'answer_preview_length'::integer) AS "AnswerPreview",
           m."CreatedAt" AS "AnswerCreatedAt",
           CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb') THEN
               CASE WHEN jsonb_typeof(m."AssistantPayloadJson"::jsonb -> 'Outcome') = 'number'
                         AND (m."AssistantPayloadJson"::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                         AND length(m."AssistantPayloadJson"::jsonb ->> 'Outcome') <= 9
                    THEN (m."AssistantPayloadJson"::jsonb ->> 'Outcome')::int
               END
           END AS "Outcome"
    FROM "Messages" AS m
    WHERE m."ConversationId" = q."ConversationId"
      AND m."Role" = 'Assistant'
      AND m."CreatedAt" >= q."CreatedAt"
      AND (n."CreatedAt" IS NULL OR m."CreatedAt" < n."CreatedAt")
    ORDER BY m."CreatedAt", m."Id"
    LIMIT 1
) AS a ON TRUE
WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
  AND a."AnswerId" IS NULL
ORDER BY q."CreatedAt" DESC, q."Id" DESC
LIMIT :'take'::integer OFFSET :'skip'::integer;
ROLLBACK;

-- [D-count] user filter (three actor ids) in the 30-day window
\echo [D-count]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT s."Value" FROM (
    SELECT count(*)::int AS "Value" FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId" WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND c."ActorId" = ANY(:'actor_ids'::uuid[])
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
) AS s
LIMIT 2;
ROLLBACK;

-- [D-page] user filter (three actor ids) in the 30-day window
\echo [D-page]
\set take 20
\set skip 0
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT q."Id" AS "TurnId",
       q."ConversationId",
       c."Title" AS "ConversationTitle",
       c."ActorId" AS "UserId",
       left(q."Content", :'question_preview_length'::integer) AS "QuestionPreview",
       q."CreatedAt" AS "QuestionCreatedAt",
       a."AnswerId",
       a."AnswerPreview",
       a."AnswerCreatedAt",
       a."Outcome"
FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
LEFT JOIN LATERAL (
    SELECT n."CreatedAt"
    FROM "Messages" AS n
    WHERE n."ConversationId" = q."ConversationId"
      AND n."Role" = 'User'
      AND (n."CreatedAt", n."Id") > (q."CreatedAt", q."Id")
    ORDER BY n."CreatedAt", n."Id"
    LIMIT 1
) AS n ON TRUE
LEFT JOIN LATERAL (
    SELECT m."Id" AS "AnswerId",
           left(m."Content", :'answer_preview_length'::integer) AS "AnswerPreview",
           m."CreatedAt" AS "AnswerCreatedAt",
           CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb') THEN
               CASE WHEN jsonb_typeof(m."AssistantPayloadJson"::jsonb -> 'Outcome') = 'number'
                         AND (m."AssistantPayloadJson"::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                         AND length(m."AssistantPayloadJson"::jsonb ->> 'Outcome') <= 9
                    THEN (m."AssistantPayloadJson"::jsonb ->> 'Outcome')::int
               END
           END AS "Outcome"
    FROM "Messages" AS m
    WHERE m."ConversationId" = q."ConversationId"
      AND m."Role" = 'Assistant'
      AND m."CreatedAt" >= q."CreatedAt"
      AND (n."CreatedAt" IS NULL OR m."CreatedAt" < n."CreatedAt")
    ORDER BY m."CreatedAt", m."Id"
    LIMIT 1
) AS a ON TRUE
WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND c."ActorId" = ANY(:'actor_ids'::uuid[])
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
ORDER BY q."CreatedAt" DESC, q."Id" DESC
LIMIT :'take'::integer OFFSET :'skip'::integer;
ROLLBACK;

-- [E-count] page 50 of the default window (size 20)
\echo [E-count]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT s."Value" FROM (
    SELECT count(*)::int AS "Value" FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId" WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
) AS s
LIMIT 2;
ROLLBACK;

-- [E-page] page 50 of the default window (size 20)
\echo [E-page]
\set take 20
\set skip 980
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT q."Id" AS "TurnId",
       q."ConversationId",
       c."Title" AS "ConversationTitle",
       c."ActorId" AS "UserId",
       left(q."Content", :'question_preview_length'::integer) AS "QuestionPreview",
       q."CreatedAt" AS "QuestionCreatedAt",
       a."AnswerId",
       a."AnswerPreview",
       a."AnswerCreatedAt",
       a."Outcome"
FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
LEFT JOIN LATERAL (
    SELECT n."CreatedAt"
    FROM "Messages" AS n
    WHERE n."ConversationId" = q."ConversationId"
      AND n."Role" = 'User'
      AND (n."CreatedAt", n."Id") > (q."CreatedAt", q."Id")
    ORDER BY n."CreatedAt", n."Id"
    LIMIT 1
) AS n ON TRUE
LEFT JOIN LATERAL (
    SELECT m."Id" AS "AnswerId",
           left(m."Content", :'answer_preview_length'::integer) AS "AnswerPreview",
           m."CreatedAt" AS "AnswerCreatedAt",
           CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb') THEN
               CASE WHEN jsonb_typeof(m."AssistantPayloadJson"::jsonb -> 'Outcome') = 'number'
                         AND (m."AssistantPayloadJson"::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                         AND length(m."AssistantPayloadJson"::jsonb ->> 'Outcome') <= 9
                    THEN (m."AssistantPayloadJson"::jsonb ->> 'Outcome')::int
               END
           END AS "Outcome"
    FROM "Messages" AS m
    WHERE m."ConversationId" = q."ConversationId"
      AND m."Role" = 'Assistant'
      AND m."CreatedAt" >= q."CreatedAt"
      AND (n."CreatedAt" IS NULL OR m."CreatedAt" < n."CreatedAt")
    ORDER BY m."CreatedAt", m."Id"
    LIMIT 1
) AS a ON TRUE
WHERE q."Role" = 'User'
  AND c."TenantId" = :'tenant_id'::uuid
  AND q."CreatedAt" >= :'from_utc'::timestamptz
  AND q."CreatedAt" < :'to_utc'::timestamptz
ORDER BY q."CreatedAt" DESC, q."Id" DESC
LIMIT :'take'::integer OFFSET :'skip'::integer;
ROLLBACK;
