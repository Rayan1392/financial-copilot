# Telegram gateway cannot deliver assistant responses when optional Bot API fields are null

## Summary

The production Telegram gateway polls updates and successfully receives HTTP 200 responses from the Iran API, but it cannot deliver the assistant response to Telegram. `sendMessage` is sent with optional JSON fields whose values are `null`; Telegram rejects the request with HTTP 400.

## Reproduction / evidence

- Linode gateway container: `financial-copilot-telegram-gateway:2026.09.03-feature130-fallback`.
- For updates `132218544`, `132218545`, and `132218546`, gateway logs show:
  - `sendChatAction` → Telegram HTTP 200.
  - Primary API `POST /api/v1/telegram/assistant/updates` → HTTP 200.
  - `sendMessage` → Telegram HTTP 400.
  - The fallback retry also returned HTTP 400.
- The chat is valid: Telegram `getChat` for chat `1056305279` returns `ok: true`.
- Reproducing the payload against Bot API confirms the exact error:
  `{"ok":false,"error_code":400,"description":"Bad Request: object expected as reply markup"}`
  when `reply_markup: null` is included.
- A request with the same response text and `parse_mode: "MarkdownV2"`, but with `reply_markup` omitted, succeeds.
- The current gateway constructs `sendMessage` payloads with `reply_markup = Markup(actions)` even when `actions` is null. Its plain-text fallback also serializes `parse_mode: null`; Telegram rejects that as `Bad Request: unsupported parse_mode`.

## Root cause

Optional Telegram Bot API properties are serialized as explicit JSON `null` values. Telegram requires absent optional properties to be omitted (specifically `reply_markup`; likewise `parse_mode` must be omitted rather than sent as null). The gateway therefore receives a valid assistant result but Telegram rejects the outbound payload before delivery.

## Expected behavior

When no actions or parse mode are present, omit `reply_markup` and `parse_mode` from the JSON request. A response that is rejected for MarkdownV2 should retry as plain text with both optional fields omitted. Successful delivery must be recorded as the completed idempotent operation.

## Acceptance criteria

1. `sendMessage` with `actions == null` omits `reply_markup` entirely.
2. Plain-text fallback omits `parse_mode` entirely (not JSON `null`).
3. Add regression tests covering null optional fields and verifying Telegram-compatible JSON.
4. Live smoke test for `کگهر را با صنعت خودش مقایسه کن` produces a visible Telegram response; gateway logs show Telegram HTTP 200 for `sendMessage` and no permanent `TelegramError`.
5. Preserve idempotency: retrying the same update does not create duplicate Telegram messages.

## Scope note

No application code was changed while diagnosing this issue; this file is the handoff for the implementation agent.
