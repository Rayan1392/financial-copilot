# Feature 149 — Admin Customer Chat History

## User story

As an authorized administrator, I want to inspect a customer's recorded AI questions and answers in Customer 360 so I can investigate support issues using the exact persisted history.

## Acceptance criteria

1. Customer 360 has Account Information (default) and Chat History tabs; existing information remains intact under the first tab.
2. Chat History lists turns newest first, with a stable tie-breaker, question/answer summaries, question time, and optional conversation title.
3. The server returns at most 20 turns per page and supplies page position/total count; Previous and Next are server-paged.
4. A row can be opened by keyboard or pointer to fetch its complete stored question and assistant response(s) on demand.
5. Turn detail is restricted by both requested tenant/user and owning conversation/message; cross-user IDs do not return content.
6. Only user and assistant messages are eligible; system, developer, and tool messages are never returned.
7. Incomplete turns remain visible with an explicit missing-answer state; history is never regenerated.
8. Full-content reads are recorded in the existing administrative security audit infrastructure without recording conversation contents.
9. Empty, loading, and error states are available. User changes reset page and expanded state.
10. Stored content is rendered as text/Markdown through an existing safe renderer; arbitrary HTML and scripts never execute.
11. The UI is Persian RTL and works at mobile, tablet, and desktop widths.
12. No duplicate conversation tables are added; existing persistence and retention/deletion behavior remain authoritative.

## Scope

Add two admin-only read endpoints under the existing `/api/v1/admin/users/{userId}` route family, application read contracts and EF queries, and a tab inside the current Customer 360 dialog. No new conversation persistence, mutation, regeneration, AI orchestration, billing, or authentication behavior is in scope.

## Verification

See `tasks.md` and `ImplementationReport.md`. Completion claims require actual test/build evidence; skipped database tests remain explicit blockers.
