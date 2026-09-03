# Feature 131 — Implementation Tasks

## Slice 1 — Application Contracts and Query Capability

### Task 1.1 — Define value-search request contracts

- Objective: Add the Application request contracts defined by the approved design.
- Files/components expected to change: `src/backend/FinancialCopilot.Application/FinancialData/` value-search contract file; related namespace registrations if required.
- Implementation notes: Define `IFinancialStatementValueSearchService`, `FinancialStatementValueSearchRequest`, and `FinancialStatementValueClue`. Keep `ProviderName`, `StatementType`, and clue fields aligned with the approved contract.
- Acceptance verification: The contracts compile and expose no public API or AI-facade dependency.

### Task 1.2 — Define result and evidence models

- Objective: Represent resolved company/statement matches, canonical line-item evidence, unresolved identity, and deterministic no-match outcomes.
- Files/components expected to change: The same Application value-search contract/model area.
- Implementation notes: Include symbol, company name, resolution status, statement metadata, provider/external identifiers, and requested value evidence with normalized metric code and original source title.
- Acceptance verification: A result can represent resolved, unresolved, and empty matches without guessing or losing required evidence.

### Task 1.3 — Implement request validation

- Objective: Validate the bounded input contract before querying the database.
- Files/components expected to change: Application value-search validation/service boundary.
- Implementation notes: Require at least one clue and a numeric value for every clue. Reject title/metric/alias-only clues. Preserve decimal precision and enforce the existing bounded input limits.
- Acceptance verification: Valid requests pass; empty clues, missing numeric values, malformed values, unsupported statement types, and excessive inputs fail deterministically.

## Slice 2 — Financial Statement Query Implementation

### Task 2.1 — Implement latest statement selection

- Objective: Select one deterministic latest persisted statement per resolved company and requested statement type/provider.
- Files/components expected to change: Infrastructure financial-statement query/repository implementation using the existing financial ingestion `DbContext` and entities.
- Implementation notes: Order by `PeriodEnd` descending, `PublishedAt` descending with nulls last, `LastSynchronizedAt` descending, and statement ID/external statement ID tie-breaker. Do not query raw provider APIs.
- Acceptance verification: An older statement is never selected when a newer eligible statement exists; ties resolve deterministically.

### Task 2.2 — Implement exact numeric and same-statement clue matching

- Objective: Match every requested clue against line items in the same selected statement.
- Files/components expected to change: Infrastructure query implementation and its Application service adapter.
- Implementation notes: Use database decimal equality. Assign a request-clue identifier to each distinct value/clue pair. Apply value and optional metric/source-item constraints to the same line-item row, then require all clue identifiers in one statement.
- Acceptance verification: Single values, multiple values, value-plus-metric/title clues, split-statement values, and exact decimal cases produce the specified results.

### Task 2.3 — Resolve governed metric/title clues

- Objective: Reuse existing governed metric catalog, aliases, and persisted source-item mappings to refine numeric line-item matching.
- Files/components expected to change: Existing semantic resolver/mapping integration point and the Feature 131 query adapter only.
- Implementation notes: Do not add mappings, fuzzy matching, substring matching, LLM resolution, or a new semantic subsystem. Conflicting revenue/operating-profit concepts must not be silently relabeled.
- Acceptance verification: Known metric codes, persisted titles, and governed aliases match correctly; conflicting or unresolved clues yield no match/validation outcome.

### Task 2.4 — Implement company resolution precedence

- Objective: Resolve the listed company for each selected statement using the approved order.
- Files/components expected to change: Infrastructure query projection using existing `FinancialStatements.CompanyId`, `Companies`, and provider/external-company mapping data.
- Implementation notes: Use valid local `CompanyId` first. Fall back to `ProviderName + ExternalCompanyId` mapping only when local resolution is absent or invalid. Preserve unresolved status when neither path works and apply existing eligibility rules.
- Acceptance verification: Local precedence, null-local fallback, and unresolved-company scenarios return the correct status and company fields.

### Task 2.5 — Group duplicate source representations

- Objective: Return one traceable canonical evidence item for duplicate normalized/provider source rows.
- Files/components expected to change: Infrastructure result projection/grouping and Application result mapper.
- Implementation notes: Treat rows with the same statement, request clue, exact value, normalized metric identity, and source-item/provider-code identity as duplicates. Select canonical evidence by populated normalized `MetricCode`, then populated `SourceItemCatalogId`, then lowest line-item ID. Retain duplicate IDs/titles only in internal diagnostics.
- Acceptance verification: Duplicate source rows never duplicate the company result or canonical item result, and diagnostic evidence remains traceable.

### Task 2.6 — Wire the internal Application service

- Objective: Register and expose the deterministic service to the existing internal operator/support workflow.
- Files/components expected to change: Application service implementation, Infrastructure dependency-injection registration, and the existing internal workflow composition root.
- Implementation notes: Invoke only through `IFinancialStatementValueSearchService`. Do not add a public route, AI parser, AI tool, screener call, identity service, or new subsystem.
- Acceptance verification: The internal workflow can invoke the service and receive the approved result contract without any new external integration boundary.

## Slice 3 — Verification

### Task 3.1 — Add Application and query unit tests

- Objective: Verify validation, clue resolution boundaries, latest selection logic, exact matching, resolution precedence, and duplicate grouping in isolation.
- Files/components expected to change: `tests/FinancialCopilot.UnitTests/` Feature 131 test file(s).
- Implementation notes: Cover localized decimal parsing, invalid title-only input, governed aliases, conflicting concepts, same-line-item constraints, and deterministic duplicate canonicalization.
- Acceptance verification: Unit tests cover all corresponding acceptance criteria without using a real external provider.

### Task 3.2 — Add persisted-database integration tests

- Objective: Verify the end-to-end read-only query against the existing financial statement entities and mappings.
- Files/components expected to change: `tests/FinancialCopilot.IntegrationTests/` Feature 131 integration test file(s) and test fixture data.
- Implementation notes: Seed latest and older statements, null and valid `CompanyId` cases, provider/external mappings, multiple clues, split statements, exact decimals, and duplicate source rows.
- Acceptance verification: Integration tests prove same-statement matching, latest selection, local precedence, fallback resolution, unresolved identity, and no-match behavior.

### Task 3.3 — Verify the production-like evidence fixture

- Objective: Confirm the approved example values produce the expected grouped evidence against persisted data.
- Files/components expected to change: Feature 131 integration fixture/test data only.
- Implementation notes: Use the verification example as test data, not as a hardcoded production result. Assert the company result, statement metadata, values, metric codes, and source titles.
- Acceptance verification: The fixture returns one company result with traceable evidence for both requested values and no duplicate company result.

