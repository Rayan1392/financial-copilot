# Feature 149 change — Fix same-timestamp turn pairing (BUG-037)

**Status: Gate A: Approved by the owner on 2026-11-20** (OQ-119 (a), together with Feature 164 Gate B). No material design change: the fix restores the turn semantics
already approved in `specs/149-admin-customer-chat-history/design.md` ("a turn's answer consists only of subsequent
persisted Assistant messages").

## Defect

`ConversationRepository.PersistExchangeAsync` writes the User and Assistant rows of one exchange with the same
`CreatedAt`. `EfCoreAdminChatHistoryQueries` breaks equal timestamps with `Id`, and ids are random `Guid.NewGuid()`
values. When the answer id sorts before the question id (about half of all exchanges), the answer counts as earlier
than its question: it is attached to the previous turn of the conversation, and the question is shown as unanswered.
The SQL predicate (`ForTurns`, PostgreSQL `uuid` order) and the in-memory grouping (`GroupAnswers`, .NET
`Guid.CompareTo` order) also compare ids in different orders. The existing test seeds ids `…01` (question) and `…02`
(answer), so it does not catch this. Found by code reading on 2026-11-20; not yet reproduced on PostgreSQL.

## Correction

Order messages by `(CreatedAt, role rank, Id)` with `User` = 0 and `Assistant` = 1 in both the SQL predicate and the
in-memory grouping, the same rule Feature 164 uses and the chat's own `MessageRepository.ListByConversationAsync`
already applies (`ThenBy(Role == User ? 0 : 1)`).

## Plan

See `tasks.md` in this folder. Affected: `EfCoreAdminChatHistoryQueries.cs`, `AdminManagementEndpointTests.cs`.
Risk: low; list and detail of the per-user chat history change only where they were wrong. Verification: regression
test with random ids and equal timestamps (in-memory host) plus the same case in Feature 164's PostgreSQL tests.
