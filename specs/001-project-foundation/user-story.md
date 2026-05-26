# User Story — Project Foundation

## Story

As a backend engineering team,  
I want a clean .NET 10 solution structure with enforced architecture boundaries,  
so that the product can evolve safely from MVP to SaaS platform.

## Acceptance Criteria

- Solution contains API, Application, Domain, Infrastructure, Worker, UnitTests, IntegrationTests, and ArchitectureTests projects.
- Project dependencies follow Clean Architecture.
- Architecture tests fail if forbidden dependencies are introduced.
- API starts successfully with health check endpoint.
- Swagger/OpenAPI is available in development environment.
- Centralized exception handling middleware exists.
- Correlation id middleware exists.
- Structured logging is configured.

## Technical Notes

- Add `FinancialCopilot.Domain` even if the initial user-provided structure did not include it.
- Use `Directory.Build.props` for shared settings.
- Enable nullable reference types.
- Treat warnings as errors where practical.
