# Feature 149 — Design Review

## Findings

### F149-DR-01 — Turn grouping depends on a stable sequence across message timestamps

**Severity: MAJOR**

The persistence model allows multiple messages to share timestamps. A grouping rule that uses timestamps alone could attach an assistant response to the wrong question or produce unstable pages.

**Resolution:** Treat `(CreatedAt, Id)` as the persisted total order everywhere. For an assistant response, the turn interval is strictly after the anchor question and strictly before the next user anchor under that same order. The list order also uses question `(CreatedAt DESC, Id DESC)`. Add equal-timestamp tests.

**Status:** Resolved in `design.md` before implementation.

### F149-DR-02 — Full historical response reads need auditable fail-closed access

**Severity: MAJOR**

Conversation content can contain sensitive data. Returning detail before the administrative access audit is persisted would allow content reads without a durable audit record.

**Resolution:** Reuse `SecurityAdminAuditRow`; persist one content-free detail-read event after ownership resolution and before returning question/assistant content. Do not log or audit content. An audit persistence failure fails the request closed. Add integration assertions for both event fields and failure behavior.

**Status:** Resolved in `design.md` before implementation.

### F149-DR-03 — List summaries can expose the full turn or internal messages

**Severity: MAJOR**

A naïve list query may load entire conversations, leak tool/system content, or create N+1 reads.

**Resolution:** List queries project only user anchors plus bounded preview text and first eligible assistant preview, page anchors in SQL, exclude all non-user/assistant roles, and use a bounded query count. Detail is separate and lazy. Do not load `ScannerQueryPlanJson` in either endpoint.

**Status:** Resolved in `design.md` before implementation.

### F149-DR-04 — Assistant structured payload may not be safely renderable as Markdown

**Severity: MINOR**

Persisted assistant payloads may contain structured data beyond plain Markdown.

**Resolution:** Return payload as data without executing/rendering HTML; reuse existing safe renderer only after confirming behavior. Use text-only fallback where payload types are unsupported.

**Status:** Resolved in `design.md` before implementation.

## Review verdict

**READY_FOR_IMPLEMENTATION** — All blocker and major findings have defined resolutions in the design. Confirm the existing Markdown renderer's HTML behavior during implementation and keep the text-only fallback if it is not demonstrably safe.
