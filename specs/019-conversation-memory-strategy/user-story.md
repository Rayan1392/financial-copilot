# User Story - Conversation Memory Strategy

## Story

As a product owner,
I want a consent-aware memory strategy separated from basic chat persistence,
so that future AI assistance can become more relevant without silently using sensitive financial preferences or weakening tenant isolation.

## Acceptance Criteria

- Phase 1 requires `Conversation` and `Message` persistence only; advanced memory is explicitly future scope.
- Future memory concepts include `ShortTermConversationMemory`, `LongTermUserMemory`, `PortfolioAwareMemory`, `PreferenceMemory`, `ResearchMemory`, and `WatchlistMemory`.
- Potential remembered context includes preferred sectors, risk appetite, investment horizon, favorite symbols, prior analyses, and watchlist behavior.
- Memory records are tenant-aware, subject-aware, purpose-scoped, versioned where appropriate, and protected according to sensitivity.
- Long-term, preference, portfolio, watchlist, and research memory require explicit consent and user-controllable policy before use in orchestration.
- The platform can explain when optional memory influenced an answer and can support controls to inspect, revoke, or delete permitted stored memory.
- AI orchestration may consume authorized memory through stable context-provider interfaces; model providers do not independently persist or retrieve product memory.
- Financial/billing/account secrets and sensitive financial preferences are not exposed through prompts or telemetry without approved protection policy.
- Memory does not replace authoritative portfolio, watchlist, conversation, billing, or financial-data records.

## Technical Notes

- Do not add advanced memory stores or vector memory to the Phase 1 scanner solely for future readiness.
- Treat memory retrieval as a controlled Application capability behind the single AI facade when later enabled.
- Research/vector memory can align with future retrieval architecture only after data protection, consent, and deletion requirements are established.
