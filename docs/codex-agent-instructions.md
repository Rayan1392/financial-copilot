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
- Do not hardcode AI model provider details inside parser, scanner, explainability, Billing, or public API contracts; use provider-neutral interfaces for hosted and local models.
- Record Usage Accounting and persist Conversation Messages for AI facade executions.
- Keep `FinancialCopilot.Billing` as an isolated bounded context/module; do not put ledger, reservation, pricing, wallet, invoice, or payment rules into AI/Scanner orchestration.
- Use immutable billing ledger entries as accounting truth and treat wallet balance only as a rebuildable read projection.
- Use operation-based, versioned pricing with reservation/commit/release semantics; do not hardcode `1 query = 1 credit`.
- Treat Abravran as a contract-pending hosted AI adapter until official API documentation is supplied; do not invent integration fields or authentication behavior.

## Project Dependency Rules

Allowed dependencies:

```text
API -> Application, Billing contracts, Infrastructure
Application -> Domain
Infrastructure -> Application, Billing contracts, Domain
Worker -> Application, Infrastructure
Billing -> Domain
API/Application -> Billing contracts as required for charging workflows
Tests -> all projects as needed
```

Not allowed:

```text
Domain -> Application
Domain -> Infrastructure
Application -> Infrastructure
AI/Scanner -> Billing persistence implementations
```

## Implementation Order

Use `specs/implementation-checklist.md` as the authoritative implementation order and progress ledger. Before starting a story, read its `user-story.md` and `tasks.md`, review current implementation state, and update the checklist item only according to its completion gate.

The checklist intentionally places Billing foundations and the Financial Semantic Layer before the workflows that depend on their contracts. It also distinguishes the Scanner MVP delivery from future platform evolution features.

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
- Billable workflow execution reserves and finalizes usage exactly once through the Billing bounded context.
- Hosted/local LLM selection occurs behind AI model provider interfaces and emits normalized usage facts for Billing.
- No direct infrastructure dependency leaks into Application/Domain.
- README/spec file updated if behavior changes.
