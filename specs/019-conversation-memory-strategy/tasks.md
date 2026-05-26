# Tasks

- Define future memory type, consent policy, sensitivity classification, tenant/subject ownership, provenance, retention, revocation, and deletion contracts.
- Define `IMemoryContextProvider`, `IMemoryConsentService`, and `IMemoryAuditService` extension points without requiring their Phase 1 implementation.
- Define orchestration rules for optionally incorporating authorized memory and disclosing material memory use in explanations.
- Define protection requirements preventing provider adapters, logs, or telemetry exports from leaking sensitive memory.
- Confirm that Phase 1 conversation persistence contracts remain independent of advanced memory implementation.
- Define future tests for consent enforcement, tenant isolation, revocation/deletion, and explanation of memory-assisted answers.
