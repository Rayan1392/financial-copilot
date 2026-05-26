# Codex Agent Instructions

## Role

You are implementing the backend for Financial Copilot, an AI-powered Iranian capital market assistant.

Act as a senior .NET backend engineer. Prioritize correctness, maintainability, testability, and domain clarity.

## Technology Stack

- .NET 10
- C#
- ASP.NET Core Web API
- PostgreSQL
- EF Core
- Redis
- RabbitMQ
- Clean Architecture
- SOLID
- React + TypeScript frontend exists separately

## Mandatory Engineering Rules

- Do not put business logic in controllers.
- Do not let AI-generated output directly execute SQL.
- Use DTOs for API contracts.
- Use domain models/value objects for financial concepts.
- Use EF Core configurations instead of bloated DbContext setup.
- Use FluentValidation or equivalent for command/query validation.
- Add unit tests for financial calculations.
- Add integration tests for scanner endpoints.
- Add architecture tests to enforce project dependencies.
- Use async/await and cancellation tokens.
- Add structured logging with correlation id.
- Add idempotency for ingestion jobs.
- Do not hardcode third-party provider details inside Application layer.

## Project Dependency Rules

Allowed dependencies:

```text
API -> Application, Infrastructure
Application -> Domain
Infrastructure -> Application, Domain
Worker -> Application, Infrastructure
Tests -> all projects as needed
```

Not allowed:

```text
Domain -> Application
Domain -> Infrastructure
Application -> Infrastructure
```

## Implementation Order

1. Create solution and project structure.
2. Add architecture test dependencies.
3. Implement Domain primitives for symbols, periods, metrics, scanner conditions.
4. Implement Application interfaces and scanner use cases.
5. Implement Infrastructure persistence and provider abstractions.
6. Implement scanner parse endpoint.
7. Implement scanner execute endpoint with deterministic mock provider/repository first.
8. Add real provider integration behind interfaces.
9. Add data ingestion worker.
10. Add usage metering.

## Definition of Done

A feature is done when:

- API endpoint works.
- Request/response DTOs are documented.
- Validation exists.
- Unit tests pass.
- Integration tests pass.
- Logs contain correlation id.
- Errors use standard problem details.
- No direct infrastructure dependency leaks into Application/Domain.
- README/spec file updated if behavior changes.
