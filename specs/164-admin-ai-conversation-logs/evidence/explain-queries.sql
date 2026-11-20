-- Feature 164, task 7.1: the list queries exactly as EfCoreAdminConversationLogQueries sends them (count + page),
-- generated from one template together with production-explain-readonly.sql. Parameters are psql variables cast to
-- the Npgsql parameter types; Npgsql sends unprepared statements, so the planner sees the values as here.
-- Values for the synthetic dataset (seed-synthetic.sql). "Last 30 Tehran days" on 2026-11-20 is
-- [2026-10-22 00:00, 2026-11-21 00:00) Asia/Tehran (UTC+03:30) = [2026-10-21T20:30Z, 2026-11-20T20:30Z).
\set tenant_id '11111111-1111-1111-1111-111111111111'
\set from_utc '2026-10-21T20:30:00Z'
\set to_utc '2026-11-20T20:30:00Z'
\set text_pattern '%سود خالص%'
-- Two heavy users and one light user of the skewed distribution: md5('user-1'), md5('user-2'), md5('user-3000').
SELECT format('{%s,%s,%s}', md5('user-1')::uuid, md5('user-2')::uuid, md5('user-3000')::uuid) AS actor_ids \gset
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
