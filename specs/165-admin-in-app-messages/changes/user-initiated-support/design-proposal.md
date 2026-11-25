# Design: User-Initiated Support Conversations

Status: Gate A: Approved by the owner on 2026-11-25

## Approved Design

Add «درخواست جدید» to the existing «پیام‌ها» inbox on desktop and mobile/PWA. A signed-in user on any plan submits a subject and a plain-text message, then lands in the conversation. The inbox empty state explains how to contact Sapio support. A failed send keeps the draft. The web inbox retains the conversation and signals unread replies from «تیم ساپیو». The feature uses no AI credits.

A new request starts in «در انتظار پاسخ» and enters the existing admin «پیام‌های کاربران» queue. The queue orders unanswered items by their oldest pending time. Viewing a request preserves its queue position. A committed staff reply removes it from the pending view and displays «پاسخ داده شد» to the user. A user follow-up in the same conversation returns it to the pending view. Existing notices and staff-started conversations retain their reply rules.

Reuse Feature 165 persistence, unread state, staff permissions, and reply handling. Store the first message and the pending state atomically. Derive the recipient from the authenticated web session. Preserve isolation between users and tenants, allow safe retry, and keep the staff revision check for concurrent replies.

Manual closure, assignment, priorities, attachments, email, and Telegram are deferred. [OQ-129](../../../open-questions.md) tracks queue ownership and the internal first-response target. The approved design makes no customer-facing response-time promise.

## Decision Record

The owner explicitly wrote «**Gate A** تایید است» on 2026-11-25 in this conversation. The approval applies to this design. The [implementation plan](tasks.md) has a separate approval gate.
