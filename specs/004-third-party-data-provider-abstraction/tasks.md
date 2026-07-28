# Tasks

- Define provider interfaces.
- Implement mock provider.
- Implement raw payload storage model.
- Add typed HttpClient configuration.
- Add resilience policies.
- Add provider health-check service/adapter; the protected HTTP endpoint is owned by `012-admin-data-operations`.
- Define batch quote/market-statistics contracts with live-versus-previous-trading-day source metadata.
- Implement deterministic mock scenarios for live quote availability and previous-trading-day fallback.

## Implementation Status - 2026-05-26

Implemented in this story:

- Added Application-facing provider contracts for symbol, financial-statement, monthly-production/sales, and batched market-quote retrieval, plus provider health, raw-payload storage, and normalized provider error types.
- Added provider-neutral quote observations identifying live quote versus previous completed trading-day fallback, with source and observation timestamps required for later scanner explanation.
- Added EF Core `ProviderRawPayloads` storage and idempotent checksum lookup/storage for auditable provider responses before downstream normalization.
- Added `MockFinancialDataProvider` as the deterministic active adapter, including raw symbol/report payloads, healthy status, live quote scenario, previous-trading-day fallback scenario, and unavailable-symbol behavior.
- Added `ConfiguredFinancialDataProviderClient` as a typed HTTP adapter for a future selected provider contract; it reads base address/API key from configuration, maps remote failures to internal error codes, and captures raw fetched report payloads.
- Added a typed HTTP resilience handler supporting configured timeouts, transient retries, and circuit breaking without exposing transport concerns to Application or Scanner code.
- Registered provider DbContext, raw-payload repository, configured typed HTTP client/resilience configuration, and mock provider interfaces in Infrastructure composition.
- Added integration tests for idempotent raw storage, deterministic mock quote/fallback/health behavior, configured HTTP quote mapping, and retry/circuit-breaker behavior.

Explicitly deferred to dependent stories:

- `005-data-ingestion-and-normalization` owns worker consumption, raw-to-normalized transforms, idempotent normalized upserts, synchronization records/errors, and metric-recalculation triggers.
- `012-admin-data-operations` owns protected provider health and synchronization HTTP endpoints.
- Activation of a real provider adapter, concrete endpoint/payload mapping, production credentials, and licensing/cache-policy decisions remain contingent on the selected provider contract; the implemented configurable typed client is the transport foundation.
