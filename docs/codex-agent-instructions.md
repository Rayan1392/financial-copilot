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
- Expose `POST /api/ai/v1/query` as the only public chat-query endpoint used by the React UI.
- Do not make the frontend select Scanner Tool or any other backend use case.
- Do not let AI-generated output directly execute SQL.
- Use DTOs for API contracts.
- Use domain models/value objects for financial concepts.
- Use EF Core configurations instead of bloated DbContext setup.
- Use FluentValidation or equivalent for command/query validation.
- Add unit tests for financial calculations.
- Add integration tests for the AI facade and generic Conversation endpoints.
- Add Application-layer tests for scanner parsing and execution services.
- Add architecture tests to enforce project dependencies.
- Use async/await and cancellation tokens.
- Add structured logging with correlation id.
- Add idempotency for ingestion jobs.
- Do not hardcode third-party provider details inside Application layer.
- Record Usage Accounting and persist Conversation Messages for AI facade executions.

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
4. Implement Application interfaces, `IAiQueryOrchestrator`, Intent Detection, Tool Routing, and scanner use cases.
5. Implement Infrastructure persistence and provider abstractions.
6. Implement scanner parser and execution services with deterministic mock provider/repository first.
7. Implement `POST /api/ai/v1/query` and generic Conversation history endpoints.
8. Add real provider integration behind interfaces.
9. Add data ingestion worker.
10. Add Usage Accounting tied to AI query execution.

## Definition of Done

A feature is done when:

- API endpoint works.
- The React UI contract uses only `POST /api/ai/v1/query` for submitted messages.
- Request/response DTOs are documented.
- Validation exists.
- Unit tests pass.
- Integration tests pass.
- Logs contain correlation id.
- Errors use standard problem details.
- Answers expose appropriate Data Citations and Confidence Score.
- Conversation and Usage Accounting records are persisted according to policy.
- No direct infrastructure dependency leaks into Application/Domain.
- README/spec file updated if behavior changes.
