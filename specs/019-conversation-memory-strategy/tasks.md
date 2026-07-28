# Tasks

## Phase 1 (Original Scope — Completed 2026-05-27)

- [x] Define future memory type, consent policy, sensitivity classification, tenant/subject ownership, provenance, retention, revocation, and deletion contracts.
- [x] Define `IMemoryContextProvider`, `IMemoryConsentService`, `IMemoryControlService`, and `IMemoryAuditService` extension points.
- [x] Define `IMemoryProtectionPolicy` and implement `ConsentAwareMemoryProtectionPolicy` with ownership, purpose, consent, retention, provider-prompt, and telemetry rules.
- [x] Define orchestration rules for optionally incorporating authorized memory and disclosing material memory use in explanations.
- [x] Define protection requirements preventing provider adapters, logs, or telemetry exports from leaking sensitive memory.
- [x] Confirm that Phase 1 conversation persistence contracts remain independent of advanced memory implementation.
- [x] Define future tests for consent enforcement, tenant isolation, revocation/deletion, and explanation of memory-assisted answers.

## Production-Ready Upgrade (Completed 2026-05-28)

### Persistence

- [x] Add `MemoryDbContext` with `MemoryConsentPolicies`, `MemoryRecords` (soft-delete via `IsDeleted`), and `MemoryAuditEvents` tables.
- [x] Add `MemoryConsentPolicyRow`, `MemoryRecordRow`, `MemoryAuditEventRow` EF row types with configurations and indexes.
- [x] Add EF migration `AddMemoryTables`.

### Infrastructure Service Implementations

- [x] Implement `EfCoreMemoryConsentService` — idempotent upsert grant, revoke with timestamp, nullable-safe query.
- [x] Implement `EfCoreMemoryRecordRepository` — internal shared repo with soft-delete, bulk-delete, and write.
- [x] Implement `EfCoreMemoryAuditService` — fire-and-forget with resilient exception swallowing and structured warning log.
- [x] Implement `EfCoreMemoryControlService` — inspect/write/delete/deleteAll, each recording an audit event.
- [x] Implement `EfCoreMemoryContextProvider` — short-term conversation memory derived from `IMessageRepository` (no extra storage, no consent required for `ShortTermConversationMemory`); durable memory filtered through `IMemoryConsentService` + `IMemoryProtectionPolicy`.

### Application Layer Extensions

- [x] Add `DeleteAllAsync(subject, correlationId, ct)` to `IMemoryControlService` and update `DisabledMemoryControlService`.
- [x] Add `WriteAsync(...)` to `IMemoryControlService` and update `DisabledMemoryControlService`.
- [x] Add `IMemoryRecordRepository` Application interface as a future extension port.
- [x] Add `MemoryDisclosures` (`IReadOnlyCollection<MemoryUseDisclosure>?`) to `AiQueryResponse`.

### AI Orchestration Integration

- [x] Inject `IMemoryContextProvider` and `IMemoryAuditService` into `AiQueryOrchestrationService`.
- [x] Retrieve authorized memory context before intent detection; derive subject from `UserId ?? ActorId`.
- [x] Enrich user message with `[Recent conversation]` and `[Stored context]` blocks for items where `MayBeIncludedInProviderPrompt = true`.
- [x] Record `MemoryAuditAction.UsedInAnswer` for each memory item used in execution.
- [x] Return `MemoryDisclosures` on `AiQueryResponse` (null when no disclosures).

### Memory Management API

- [x] Add `GET /api/v1/memory/consent` — list consent status for all type/purpose combinations.
- [x] Add `POST /api/v1/memory/consent` — grant consent with optional expiry.
- [x] Add `DELETE /api/v1/memory/consent/{type}/{purpose}` — revoke consent.
- [x] Add `GET /api/v1/memory/records` — inspect non-deleted memory records.
- [x] Add `POST /api/v1/memory/records` — explicitly write a new memory record.
- [x] Add `DELETE /api/v1/memory/records/{memoryId}` — soft-delete one record.
- [x] Add `DELETE /api/v1/memory/records` — soft-delete all records for the current user.
- [x] Enforce `ActorType.User` guard on all endpoints (API clients receive 403).
- [x] Add `MemoryDisclosures` field to `AiQueryHttpResponse` and map in `AiFacadeController`.

### DI and Test Infrastructure

- [x] Register `MemoryDbContext` + real implementations in `ServiceCollectionExtensions`; remove disabled stubs.
- [x] Extend `AiFacadeApiFactory` to replace `MemoryDbContext` with in-memory database for all existing integration tests.
- [x] Add `MemoryImplementationTests` (11 tests) covering consent service, context provider, audit service, and control service with EF Core in-memory.
- [x] Add `MemoryManagementEndpointTests` (8 tests) covering grant/revoke consent, API-client 403, write/delete/deleteAll records, memory disclosure in query response, and validation errors.
