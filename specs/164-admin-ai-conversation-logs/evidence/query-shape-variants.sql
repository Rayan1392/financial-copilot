-- Feature 164, task 7.1 follow-up: query-shape experiments run on the synthetic set (seed-synthetic.sql) after the
-- measurement of explain-queries.sql. NOT what the code sends; evidence for the owner's decision only. See
-- measurement-2026-11-20.md, section "Query-shape experiments".
--   V-*-page-first: choose the page of turns in a sub-select (ORDER BY ... LIMIT/OFFSET), then look up the answers
--                   for those rows only (list without status filter).
--   V-C1-count-single-parse: the payload is parsed once per turn (OFFSET 0 sub-select) instead of up to four times.
--   *-jit-off: the same statement with SET LOCAL jit = off.

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

\echo [V-C1-count-current-jit-off]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '120s';
SET LOCAL jit = off;
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

\echo [V-C1-count-single-parse]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '120s';
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
           (SELECT CASE WHEN jsonb_typeof(p.j -> 'Outcome') = 'number'
                         AND (p.j ->> 'Outcome') ~ '^-?[0-9]+$'
                         AND length(p.j ->> 'Outcome') <= 9
                    THEN (p.j ->> 'Outcome')::int END
            FROM (SELECT CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb')
                              THEN m."AssistantPayloadJson"::jsonb END AS j OFFSET 0) AS p) AS "Outcome"
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

\echo [V-C1-count-single-parse-jit-off]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '120s';
SET LOCAL jit = off;
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
           (SELECT CASE WHEN jsonb_typeof(p.j -> 'Outcome') = 'number'
                         AND (p.j ->> 'Outcome') ~ '^-?[0-9]+$'
                         AND length(p.j ->> 'Outcome') <= 9
                    THEN (p.j ->> 'Outcome')::int END
            FROM (SELECT CASE WHEN pg_input_is_valid(m."AssistantPayloadJson", 'jsonb')
                              THEN m."AssistantPayloadJson"::jsonb END AS j OFFSET 0) AS p) AS "Outcome"
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

\echo [V-C2-count-jit-off]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '120s';
SET LOCAL jit = off;
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

\set take 20
\set skip 0
\echo [V-A-page-first]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '120s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT q."Id" AS "TurnId",
       q."ConversationId",
       q."Title" AS "ConversationTitle",
       q."ActorId" AS "UserId",
       left(q."Content", :'question_preview_length'::integer) AS "QuestionPreview",
       q."CreatedAt" AS "QuestionCreatedAt",
       a."AnswerId",
       a."AnswerPreview",
       a."AnswerCreatedAt",
       a."Outcome"
FROM (
    SELECT q."Id", q."ConversationId", q."Content", q."CreatedAt", c."Title", c."ActorId"
    FROM "Messages" AS q
    JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
    WHERE q."Role" = 'User'
      AND c."TenantId" = :'tenant_id'::uuid
      AND q."CreatedAt" >= :'from_utc'::timestamptz
      AND q."CreatedAt" < :'to_utc'::timestamptz
    ORDER BY q."CreatedAt" DESC, q."Id" DESC
    LIMIT :'take'::integer OFFSET :'skip'::integer
) AS q
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
ORDER BY q."CreatedAt" DESC, q."Id" DESC;
ROLLBACK;

\set take 20
\set skip 0
\echo [V-B-page-first]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '120s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT q."Id" AS "TurnId",
       q."ConversationId",
       q."Title" AS "ConversationTitle",
       q."ActorId" AS "UserId",
       left(q."Content", :'question_preview_length'::integer) AS "QuestionPreview",
       q."CreatedAt" AS "QuestionCreatedAt",
       a."AnswerId",
       a."AnswerPreview",
       a."AnswerCreatedAt",
       a."Outcome"
FROM (
    SELECT q."Id", q."ConversationId", q."Content", q."CreatedAt", c."Title", c."ActorId"
    FROM "Messages" AS q
    JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
    WHERE q."Role" = 'User'
      AND c."TenantId" = :'tenant_id'::uuid
      AND q."CreatedAt" >= :'from_utc'::timestamptz
      AND q."CreatedAt" < :'to_utc'::timestamptz
      AND translate(q."Content", 'يىك', 'ییک') ILIKE :'text_pattern'::text ESCAPE '\'
    ORDER BY q."CreatedAt" DESC, q."Id" DESC
    LIMIT :'take'::integer OFFSET :'skip'::integer
) AS q
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
ORDER BY q."CreatedAt" DESC, q."Id" DESC;
ROLLBACK;

\set take 20
\set skip 0
\echo [V-D-page-first]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '120s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT q."Id" AS "TurnId",
       q."ConversationId",
       q."Title" AS "ConversationTitle",
       q."ActorId" AS "UserId",
       left(q."Content", :'question_preview_length'::integer) AS "QuestionPreview",
       q."CreatedAt" AS "QuestionCreatedAt",
       a."AnswerId",
       a."AnswerPreview",
       a."AnswerCreatedAt",
       a."Outcome"
FROM (
    SELECT q."Id", q."ConversationId", q."Content", q."CreatedAt", c."Title", c."ActorId"
    FROM "Messages" AS q
    JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
    WHERE q."Role" = 'User'
      AND c."TenantId" = :'tenant_id'::uuid
      AND c."ActorId" = ANY(:'actor_ids'::uuid[])
      AND q."CreatedAt" >= :'from_utc'::timestamptz
      AND q."CreatedAt" < :'to_utc'::timestamptz
    ORDER BY q."CreatedAt" DESC, q."Id" DESC
    LIMIT :'take'::integer OFFSET :'skip'::integer
) AS q
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
ORDER BY q."CreatedAt" DESC, q."Id" DESC;
ROLLBACK;

\set take 20
\set skip 980
\echo [V-E-page-first]
BEGIN;
SET TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '120s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT q."Id" AS "TurnId",
       q."ConversationId",
       q."Title" AS "ConversationTitle",
       q."ActorId" AS "UserId",
       left(q."Content", :'question_preview_length'::integer) AS "QuestionPreview",
       q."CreatedAt" AS "QuestionCreatedAt",
       a."AnswerId",
       a."AnswerPreview",
       a."AnswerCreatedAt",
       a."Outcome"
FROM (
    SELECT q."Id", q."ConversationId", q."Content", q."CreatedAt", c."Title", c."ActorId"
    FROM "Messages" AS q
    JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
    WHERE q."Role" = 'User'
      AND c."TenantId" = :'tenant_id'::uuid
      AND q."CreatedAt" >= :'from_utc'::timestamptz
      AND q."CreatedAt" < :'to_utc'::timestamptz
    ORDER BY q."CreatedAt" DESC, q."Id" DESC
    LIMIT :'take'::integer OFFSET :'skip'::integer
) AS q
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
ORDER BY q."CreatedAt" DESC, q."Id" DESC;
ROLLBACK;

-- Text pre-filter variant (answer lookup returns the raw payload; outcome checked outside):
\echo raw-payload-lateral-with-like-prefilter
BEGIN; SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '120s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*)::int
FROM "Messages" AS q
JOIN "Conversations" AS c ON c."Id" = q."ConversationId"
LEFT JOIN LATERAL (
    SELECT n."CreatedAt" FROM "Messages" AS n
    WHERE n."ConversationId" = q."ConversationId" AND n."Role" = 'User'
      AND (n."CreatedAt", n."Id") > (q."CreatedAt", q."Id")
    ORDER BY n."CreatedAt", n."Id" LIMIT 1
) AS n ON TRUE
LEFT JOIN LATERAL (
    SELECT m."Id" AS "AnswerId", m."AssistantPayloadJson" AS "Payload"
    FROM "Messages" AS m
    WHERE m."ConversationId" = q."ConversationId" AND m."Role" = 'Assistant'
      AND m."CreatedAt" >= q."CreatedAt" AND (n."CreatedAt" IS NULL OR m."CreatedAt" < n."CreatedAt")
    ORDER BY m."CreatedAt", m."Id" LIMIT 1
) AS a ON TRUE
WHERE q."Role" = 'User' AND c."TenantId" = '11111111-1111-1111-1111-111111111111'
  AND q."CreatedAt" >= '2026-10-21T20:30:00Z' AND q."CreatedAt" < '2026-11-20T20:30:00Z'
  AND a."Payload" LIKE '%"Outcome":7%'
  AND CASE WHEN pg_input_is_valid(a."Payload", 'jsonb') THEN
          CASE WHEN jsonb_typeof(a."Payload"::jsonb -> 'Outcome') = 'number'
                    AND (a."Payload"::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                    AND length(a."Payload"::jsonb ->> 'Outcome') <= 9
               THEN (a."Payload"::jsonb ->> 'Outcome')::int END END = 7;
ROLLBACK;

-- Set-based variant (window function instead of per-turn lookups):
\echo set-based-failed-count
BEGIN; SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '120s';
EXPLAIN (ANALYZE, BUFFERS)
SELECT count(*) FROM (
  SELECT t."AssistantPayloadJson" AS p
  FROM (
    SELECT m."Role", m."AssistantPayloadJson",
           lag(m."Role") OVER w AS prev_role,
           lag(m."CreatedAt") OVER w AS prev_created
    FROM "Messages" AS m
    JOIN "Conversations" AS c ON c."Id" = m."ConversationId"
    WHERE c."TenantId" = '11111111-1111-1111-1111-111111111111'
      AND m."CreatedAt" >= '2026-10-21T20:30:00Z'
      AND m."Role" IN ('User', 'Assistant')
    WINDOW w AS (PARTITION BY m."ConversationId"
                 ORDER BY m."CreatedAt", CASE m."Role" WHEN 'User' THEN 0 ELSE 1 END, m."Id")
  ) AS t
  WHERE t."Role" = 'Assistant' AND t.prev_role = 'User' AND t.prev_created < '2026-11-20T20:30:00Z'
) AS f
WHERE CASE WHEN pg_input_is_valid(f.p, 'jsonb') THEN
          CASE WHEN jsonb_typeof(f.p::jsonb -> 'Outcome') = 'number'
                    AND (f.p::jsonb ->> 'Outcome') ~ '^-?[0-9]+$'
                    AND length(f.p::jsonb ->> 'Outcome') <= 9
               THEN (f.p::jsonb ->> 'Outcome')::int END END = 7;
ROLLBACK;
