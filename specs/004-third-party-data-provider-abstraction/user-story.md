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

## Technical Notes

- Implement mock provider first if real provider contract is not available.
- Use typed HttpClient.
- Add provider health checks.
