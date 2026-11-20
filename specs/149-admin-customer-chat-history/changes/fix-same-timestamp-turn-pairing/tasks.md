# Tasks: Fix same-timestamp turn pairing (BUG-037)

Status: Gate B: Approved by the owner on 2026-11-20 (OQ-119 (a))

## Feature Folder

`149-admin-customer-chat-history/changes/fix-same-timestamp-turn-pairing` — implemented on branch
`feature/164-admin-ai-conversation-logs` as section 9 of `specs/164-admin-ai-conversation-logs/tasks.md`.

## Implementation Tasks

- [x] 1 Add a failing regression test to `AdminManagementEndpointTests`: two exchanges in one conversation, each question and answer with equal `CreatedAt`, answer ids chosen to sort before their question ids; expect each answer under its own question and no incomplete turn.
- [x] 2 In `EfCoreAdminChatHistoryQueries`, order by `(CreatedAt, role rank, Id)` in `ForTurns` and `GroupAnswers` and in the detail answer ordering.
- [x] 3 Run the 149 tests and the Feature 164 PostgreSQL pairing test.

## Definition Of Done

- [x] Regression test fails before and passes after the fix; all existing `AdminManagementEndpointTests` green.
- [ ] BUG-037 in `specs/bugs.md` set to fixed with the commit reference.
