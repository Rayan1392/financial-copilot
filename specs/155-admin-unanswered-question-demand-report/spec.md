# Feature 155 — Unanswered Question Demand Report (Admin)

Status: **Proposed** (registered 2026-11-09). Method and reference SQL proven with a manual read-only baseline run on production data. The admin panel implementation has not started.

## User story

As a product owner or DataAdmin, I want to see which customer questions the AI assistant does not answer, grouped into roadmap themes and ranked by how many distinct customers asked them, so that I can prioritise development by real demand and re-run the report whenever I need fresh numbers.

## Problem

Persisted chat turns already record the dialogue outcome (`AssistantMessagePayload.Outcome`, `OutcomeReasonCode`, `Intent`, `SemanticCapabilityCode`). Nobody aggregates them. The baseline run surfaced three things a raw outcome count hides:

1. **Internal and automated traffic dominates volume.** Owner/admin testing and the Telegram gateway's automated monthly-sales posts are most turns. Without channel separation they mask real demand.
2. **"Answered" is not always answered.** Some turns are marked `Answered`, but the reply says the value is unavailable (*soft non-answer*). Others went to an unrelated capability (*misroute*), e.g. a technical-analysis question answered with a sales table.
3. **Several gaps are switched-off capabilities, not missing ones.** One example is `comprehensive_analysis_unavailable` in the NoavaranAmin provider mode, or a retired PS/Gauge route. These belong in a different roadmap lane than brand-new capabilities.

## Acceptance criteria

1. An admin-only report shows, for a selectable window (default 30 days, maximum 180 days):
   - headline per channel: turns, distinct users, gap turns, gap rate;
   - customer coverage buckets: `answered`, `unsupported`, `no_data`, `clarification`, `misrouted`, `soft_no_answer`, `failed`;
   - a roadmap ranking of themes T01–T15/T99, ordered by distinct users and then turns, with the bucket breakdown and last-seen date;
   - root cause per (bucket, reason code, capability);
   - up to 10 deduplicated example questions per theme, with how many times each was asked and by how many users;
   - the weekly gap-rate trend.
2. Channels are `customer`, `internal` (any role whose name contains `admin`) and `automation` (configured API-key client actor IDs). The ranking uses `customer` only. The other two stay visible in the headline.
3. Classification follows **classifier v1** exactly as defined in [design.md](design.md) and [queries.sql](queries.sql). Every rule change bumps the classifier version shown in the report.
4. Results reproduce `queries.sql` for the same window: same per-theme users/turns, same bucket counts. A parity test pins this behaviour.
5. Example questions are customer content. They require the `admin.users.read` policy (same as Feature 149). Viewing examples is audited in `auth_security_admin_audits` without storing question text. The report never shows user identity, email, or actor ID, only distinct counts.
6. The report is read-only and bounded. It uses a window cap, a statement timeout, an examples cap, and no unbounded result sets. It never regenerates answers, calls an LLM, or writes to conversation tables.
7. A CSV export of the theme ranking and the examples is available, using the same columns as the screen.
8. The UI is Persian RTL, sits as a tab of the existing admin analytics area, and works at mobile, tablet, and desktop widths.

## Out of scope

- Changing how outcomes are persisted. A projection is proposed as phase 2 in design.md.
- LLM-based clustering of questions. Classifier v1 is deterministic, and LLM clustering may be a later, separate decision.
- Fixing the gaps themselves. Each theme becomes its own feature or bug after prioritisation.

## Relationship to other features

- **028 Missing-Answer Feedback Pipeline:** captures scanner-side misses at query time into `MissingAnswerFeedbacks` (its Phase 1 collector default is a no-op). Feature 155 is a retrospective view over persisted conversation outcomes and does not replace 028. Whether 028 has production rows was **not verified** (production DB became unreachable during the baseline run). See task 155-T01.
- **117 AI Dialogue Outcome Safety:** owns `DialogueOutcome` and the reason codes this feature reads.
- **149 Admin Customer Chat History:** same authorization and audit pattern, same turn semantics (user + assistant message of one exchange).
- **v1.5.0 admin user analytics panel:** host for the new tab.

## Verification

See [tasks.md](tasks.md). Baseline evidence (2026-11-09): `queries.sql` was run on a disposable local PostgreSQL loaded with a read-only production snapshot. It matched the Python prototype classification exactly at the aggregate level (193 customer gap turns, same bucket and per-theme counts). The baseline report itself contains customer question text, so it is kept **outside this public repository**.
