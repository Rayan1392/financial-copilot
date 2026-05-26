# User Story — Third-Party Data Provider Abstraction

## Story

As a backend service,  
I want third-party financial data access behind stable interfaces,  
so that provider changes do not affect scanner business logic.

## Acceptance Criteria

- Application layer defines provider interfaces.
- Infrastructure implements provider clients.
- Provider responses can be stored as raw payloads.
- Provider calls use retry/timeouts/circuit-breaker policies.
- Provider errors are logged and mapped to internal error types.
- Provider credentials are read from configuration/secrets, never hardcoded.
- Provider abstraction supports batch retrieval of latest tradable price and price change for a set of symbols when low-latency/live quote data is available.
- When live quote data is unavailable, the provider/repository abstraction can return the latest completed trading-day market statistics with observation date and source/freshness metadata.

## Technical Notes

- Implement mock provider first if real provider contract is not available.
- Use typed HttpClient.
- Implement provider health-check capability consumed by protected admin operations in `012-admin-data-operations`.
- Keep live quote capability and last-completed-trading-day fallback behind Application-facing interfaces; the Scanner Use Case must not depend on provider-specific endpoints.
