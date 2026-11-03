# Discovery Notes

The existing Telegram transport was split across the Worker polling process, the notification
transport, and the channel-membership provider. The API already owns normalized assistant updates,
link confirmation, conversation state, notification outbox state, retries, and audit records.

The first implementation uses long polling on VPS 2. The Gateway forwards normalized updates to the
existing API endpoints over HTTPS and exposes signed operation endpoints for VPS 1. The Gateway has
no database, Redis, RabbitMQ, or business-state dependency; it persists only the Telegram polling
offset and delivery idempotency metadata locally on VPS 2.

Canonical fields carried across the boundary are update id, Telegram user/chat/message identifiers,
correlation id, idempotency key, rendered message parts, and bounded/redacted error categories.
