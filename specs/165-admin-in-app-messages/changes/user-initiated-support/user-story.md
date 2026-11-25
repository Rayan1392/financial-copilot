# User Story: Start and Follow Up on a Support Request

## Feature Folder

`165-admin-in-app-messages/changes/user-initiated-support`

## Layers / Area

backend, frontend · Conversations, Administration

## Priority

P2 (proposed planning classification)

## Complexity

M

## Depends On

Feature 165 inbox, shared support queue, and Auth schema. The baseline feature still has open rollout and verification checks. See [implementation dependencies](tasks.md#dependencies-and-handoff).

## References

- [Approved change design](design-proposal.md)
- [Feature 165 baseline story](../../user-story.md)
- [Feature 165 baseline tasks](../../tasks.md)
- [Architecture](../../../../docs/architecture.md)
- [Domain glossary](../../../../docs/domain-glossary.md)
- [Open decisions](../../../open-questions.md)

## Stories

- As a signed-in user, I want to contact Sapio from my inbox and see the reply and conversation history so I can follow up in one place.
- As an authorized support operator, I want new requests to appear in the shared queue so I can answer them in order and another operator can handle them when needed.

## Acceptance Criteria

1. The desktop and mobile/PWA inbox exposes «درخواست جدید» for any signed-in web user with active membership, irrespective of plan or AI credit balance. The empty state explains the purpose of the action.
2. The form requires a trimmed subject and body, using Feature 165 limits of 120 and 2,000 characters. Fields have visible labels, keyboard access and associated validation errors. The submit button reads «ارسال به پشتیبانی». Successful submission opens the created conversation. A failed submission keeps the draft and permits safe retry.
3. The new conversation includes the user's first message and shows «در انتظار پاسخ» in the inbox and detail. It enters «نیازمند رسیدگی» immediately. Additional user messages preserve the oldest unanswered timestamp. Staff opening or refreshing the item leaves it pending.
4. A committed staff reply removes the item from the pending queue, creates an unread staff message, and changes the user's status to «پاسخ داده شد». A follow-up by the user returns it to «در انتظار پاسخ». Reloading retains history and status. The existing read acknowledgment updates the badge independently of request status.
5. Identical sequential or concurrent retries by the same tenant actor return the original receipt. A reused key with different content or a different operation conflicts. Concurrent staff replies against the same reviewed revision still produce one committed reply, with the other operator's draft preserved for review.
6. The server derives tenant and owner from the signed-in actor, enforces active membership and the existing write rate limit, and rejects API keys. Unrelated user or tenant requests cannot read, acknowledge or append to the conversation. Existing support permission and audit rules apply to staff.
7. Existing one-way notices and staff-started support conversations retain their behavior. Request status labels apply to user-initiated conversations. Manual closure, assignment, priorities, attachments, external notifications and response-time promises are outside this change.
