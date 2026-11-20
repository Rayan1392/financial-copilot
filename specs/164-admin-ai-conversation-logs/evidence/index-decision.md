# Feature 164 — task 8.1 index decision (2026-11-20)

Status: decision rule applied to the synthetic measurement; production check (task 7.2) pending. The migration
(task 8.2) is not written yet.

## Rule (tasks.md 8.1)

Add the index if the default 30-day page exceeds 300 ms on the synthetic set, **or** a plan shows a sequential scan
plus sort of `Messages` for it.

## Result

| Default 30-day page (scenario A) | Count | Page | Count + page |
|---|---:|---:|---:|
| Without index | 107 ms | 88 ms | 195 ms |
| With the index below (after `VACUUM ANALYZE`) | 65 ms | 10 ms | 75 ms |

- Time condition: **not met** (195 ms < 300 ms).
- Plan condition: **met.** Both statements start with `Parallel Seq Scan on "Messages" q` (filter on `Role` and
  `CreatedAt`, 453,051 rows removed), and the page sorts the 40,894 window questions (`Sort Key: q."CreatedAt" DESC,
  q."Id" DESC`) before `LIMIT 20`. The scan grows with the whole `Messages` table, not with the window.

**Decision: add the index (task 8.2 runs).**

## Recommended index

```sql
CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Messages_User_CreatedAt_Id"
    ON "Messages" ("CreatedAt", "Id") INCLUDE ("ConversationId")
    WHERE "Role" = 'User';
-- Down: DROP INDEX CONCURRENTLY IF EXISTS "IX_Messages_User_CreatedAt_Id";
```

Why this shape:

- Partial on `Role = 'User'`: the list only reads questions; Assistant inserts (the larger, payload-heavy rows) do not
  maintain it. 14 MB for 250,000 questions on the synthetic set.
- `("CreatedAt", "Id")` ascending: a B-tree is read backwards for `ORDER BY q."CreatedAt" DESC, q."Id" DESC`, so the
  page reads 20 entries and stops (A page 88 → 10 ms). It also serves the range filter of every count.
- `INCLUDE ("ConversationId")`: the count joins to `Conversations` for the tenant; with the column in the index the
  count is an index-only scan once the visibility map is current (A count 107 → 65 ms). Autovacuum keeps it current in
  production; without it the plan falls back to a bitmap heap scan, still bounded by the window.
- EF Core model: `HasIndex(m => new { m.CreatedAt, m.Id }).HasFilter("\"Role\" = 'User'").IncludeProperties(m =>
  m.ConversationId).HasDatabaseName("IX_Messages_User_CreatedAt_Id")` on `MessageRow`, and a migration that runs the
  `CONCURRENTLY` statements with `suppressTransaction: true`, as planned in 8.2.

## What the index does not fix (owner decision needed)

The measurement found two problems an index cannot solve (details and plans in `measurement-2026-11-20.md`):

1. **Status-filtered count exceeds the 5 s statement timeout.** `status=Failed` over 30 days: 8,837 ms without the
   cap; with the cap the request fails. `NoResponse`: 1.2–1.6 s count, 1.1–2.1 s page. Every turn in the window needs
   its first answer and that answer's `Outcome` parsed from `AssistantPayloadJson`. Measured SQL-only alternatives:
   parse once per turn 3.3–4.4 s; text pre-filter 2.6 s; set-based window function 5.9 s. Options:
   - (a) store the outcome in a nullable column on `Messages` when the answer is saved, plus a backfill — fast and
     indexable, but a schema change the approved design excluded ("no new column"); needs Gate approval.
   - (b) keep the SQL and bound the cost: with a status filter, count at most N matches (show "more than N") or
     require a shorter date range (for example 7 days; the cost is proportional to the turns in the range, so about a quarter — estimated, not measured).
   - (c) accept the timeout for wide status queries at this volume and show a clear "narrow the range" error.
   Which volume production has is unknown until `production-explain-readonly.sql` comes back.
2. **Answer lookups run before the sort** for text search, user filter and deep pages (3,304 / 879 / 1,000 lookups
   instead of 20). Rewriting the page query to choose the page of turns first and then look up answers (no status
   filter) brings, with the index: default page 6 ms, user filter 10 ms, page 50 9 ms (was 272 ms), text search
   286 ms (180 ms without the index; was 762 ms, 1,254 ms with the index alone). This is a change to `EfCoreAdminConversationLogQueries` (no schema change) and
   needs no new decision beyond the approved design; it was not made here.

With the index alone, text search (B) and the NoResponse page (C2) were measured slower than without it, because
the planner switches to a bitmap scan that still feeds every match into the lookups. The page-first rewrite removes
that effect, so the index and the rewrite should land together.
