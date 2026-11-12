# Recover stale Telegram conversation bindings

Status: Gate A: Approved by the project owner on 2026-11-12

**Layers / Area:** backend · Telegram, Conversations

The primary API logged two `ConversationNotFoundException` failures for the same conversation on 2026-11-12. The Telegram adapter reuses the active conversation binding without checking that the conversation remains available to the linked tenant and actor. The V2 workflow rejects the unavailable conversation before reserving usage credits.

When an active binding points to a conversation no longer available to the same tenant and actor, create a fresh conversation, update the existing binding, and process the current message once. Keep healthy bindings and replay behavior intact. This is an API hotfix with no schema migration or gateway image change.
