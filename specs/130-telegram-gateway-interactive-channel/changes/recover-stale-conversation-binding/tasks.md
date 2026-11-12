# Tasks: Recover stale Telegram conversation bindings

Status: Gate B: Approved by the project owner on 2026-11-12

**Feature folder:** `130-telegram-gateway-interactive-channel/changes/recover-stale-conversation-binding`

**Layers / Area:** backend · Telegram, Conversations

**Depends on:** Feature 130 conversation-binding and replay behavior (`Design.md` and `Story.md` in the parent feature).

- [x] In `TelegramAiAssistantAdapter`, use the existing conversation repository to validate an active binding by conversation ID, tenant and actor before orchestration. On a miss, create a new conversation in that scope and replace the stale pointer. Guard the replacement against a concurrent repair and reuse the winning binding if it changes. Preserve the original message, update correlation, billing operation identity and processed-update replay. Genuine repository errors keep the existing error handling.
- [x] Add regression coverage for stale-binding recovery, subsequent reuse, replay without a second orchestration call, and concurrent repair. The Telegram adapter suite passed (21 tests), and the Telegram API integration suite passed (2 tests, including the relational update).
- [x] Compare the Release backend suite against `specs/test-baseline.md` and an unchanged baseline. Both full runs had the same architecture failures, 33 shared unit failures, and 186 shared integration failures. The two additional failures observed in the hotfix run passed on isolated reruns of both revisions.
- [ ] Merge the verified fix into `develop` and then `prod` per `docs/release/release-process.md`. The release manager records the user-facing changelog entry, verifies the next available patch tag, runs the changelog checks, and pushes the tag on the approved `prod` commit.
- [ ] Watch the production deployment workflow, verify API health and the deployed version, and verify an end-to-end Telegram message from the affected linked account. Use its new correlation ID to confirm the result.
