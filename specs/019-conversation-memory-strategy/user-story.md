# User Story - Conversation Memory Strategy

## Story

As a product owner,
I want a consent-aware memory strategy separated from basic chat persistence,
so that future AI assistance can become more relevant without silently using sensitive financial preferences or weakening tenant isolation.

## Acceptance Criteria

- [x] Phase 1 requires `Conversation` and `Message` persistence only; advanced memory is explicitly future scope. *(Phase 1 contracts kept independent.)*
- [x] Memory concepts include `ShortTermConversationMemory`, `LongTermUserMemory`, `PortfolioAwareMemory`, `PreferenceMemory`, `ResearchMemory`, and `WatchlistMemory`.
- [x] Potential remembered context includes preferred sectors, risk appetite, investment horizon, favorite symbols, prior analyses, and watchlist behavior.
- [x] Memory records are tenant-aware, subject-aware, purpose-scoped, versioned, and protected according to sensitivity.
- [x] Long-term, preference, portfolio, watchlist, and research memory require explicit consent and user-controllable policy before use in orchestration.
- [x] The platform explains when optional memory influenced an answer (`MemoryDisclosures` on `AiQueryResponse`/`AiQueryHttpResponse`).
- [x] Users can inspect, revoke, and delete permitted stored memory via `GET/DELETE /api/v1/memory/consent` and `GET/DELETE /api/v1/memory/records`.
- [x] AI orchestration consumes authorized memory through `IMemoryContextProvider`; model providers do not independently persist or retrieve product memory.
- [x] `SensitiveFinancial` and `RestrictedSecret` memory is never included in provider prompts (`MayBeIncludedInProviderPrompt = false`).
- [x] Memory does not replace authoritative portfolio, watchlist, conversation, billing, or financial-data records.

## Implementation Status

**Production-ready as of 2026-05-28.** All acceptance criteria met.

### What Was Built

| Layer | Artifact |
|---|---|
| Persistence | `MemoryDbContext` · `MemoryConsentPolicies` · `MemoryRecords` (soft-delete) · `MemoryAuditEvents` · EF migration `AddMemoryTables` |
| Infrastructure | `EfCoreMemoryConsentService` · `EfCoreMemoryRecordRepository` · `EfCoreMemoryAuditService` · `EfCoreMemoryControlService` · `EfCoreMemoryContextProvider` |
| Orchestration | Memory retrieval before intent detection · message enrichment (`[Recent conversation]` / `[Stored context]`) · post-execution audit · `MemoryDisclosures` on response |
| API | 7 endpoints on `/api/v1/memory/…` restricted to `ActorType.User`; API clients return 403 |
| Tests | Unit 168 (unchanged) · Integration 135 (+19 new) · Architecture 3 (unchanged) |

### Deferred (Out of Scope per Story)

- Vector/semantic search across memory items
- LLM-based automatic memory capture from conversations
- Cross-tenant or cross-subject memory federation
- External vector stores or ML feature platforms

## Technical Notes

- Short-term conversation memory is derived from `IMessageRepository` — no extra storage or consent required.
- `ConsentAwareMemoryProtectionPolicy` enforces: owner match, purpose match, consent status, retention expiry, prompt-safety gate, and telemetry-safety gate.
- Memory does not replace or bypass the authoritative `Conversation`/`Message` persistence established in `007`.
- Research/vector memory aligns with future retrieval architecture only after data protection, consent, and deletion requirements are established.
