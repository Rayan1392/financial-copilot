# Feature 131 — Financial Statement Value Search

## User Story

As an operator/support workflow consumer, I want to provide one or more exact financial-statement numeric clues with an optional governed metric or source-title clue, so that I can identify the listed company whose latest persisted statement contains those clues and inspect the supporting line-item evidence.

## Scope

### Included

- An internal Application service invoked by an operator/support workflow.
- `IncomeStatement` as the initial statement type.
- One or more exact decimal numeric clues.
- An optional existing governed metric code, persisted source title, or governed alias per numeric clue.
- Deterministic latest-statement selection using the approved ordering.
- Exact database-numeric comparison without rounding, tolerance, formatting, or currency conversion.
- Requiring every requested clue to match a line item in the same selected statement.
- Company resolution using valid `FinancialStatements.CompanyId` first, then the existing provider/external-company mapping.
- One grouped company/statement result with matched line-item evidence.
- Deterministic unresolved-company and no-match outcomes.

### Excluded

- Public API routes or AI query-facade integration.
- Title, metric, or alias searches without a numeric value.
- Explicit historical period or statement-ID lookup.
- Fuzzy matching, rounding, tolerance, currency conversion, or unrelated statement types.
- Raw provider API calls at query time.
- Data repair, correction, or ingestion workflows.
- New semantic infrastructure, LLM reasoning, generic financial search, screening, analytics, or knowledge-graph capabilities.

## Functional Behavior

### Input

The service accepts `FinancialStatementValueSearchRequest`:

```csharp
public sealed record FinancialStatementValueSearchRequest(
    string ProviderName,
    FinancialStatementType StatementType,
    IReadOnlyCollection<FinancialStatementValueClue> Clues);

public sealed record FinancialStatementValueClue(
    decimal Value,
    string? MetricCode,
    string? SourceTitle,
    string? GovernedAlias);
```

`Clues` must contain at least one numeric value. Each clue may optionally include an existing governed metric, persisted source title, or governed alias. `IncomeStatement` is the default statement type and the configured financial-statement provider is used.

### Processing

1. Resolve each optional clue through existing governed metric/source-item mappings.
2. Resolve company identity using valid `FinancialStatements.CompanyId` first, followed by `ProviderName + ExternalCompanyId` mapping. If neither resolves, retain only an unresolved diagnostic outcome.
3. Select the latest statement per resolved company and statement type, ordered by `PeriodEnd`, `PublishedAt` with nulls last, `LastSynchronizedAt`, and deterministic statement ID tie-breaker.
4. Compare every clue value exactly against persisted database numeric values.
5. Apply the optional metric/title constraint to the same line-item row as its numeric value.
6. Require every clue to match within the same selected latest statement.
7. Group duplicate source representations into one canonical evidence item while retaining duplicate identifiers/titles only for diagnostics.

### Output

The service returns `FinancialStatementValueSearchResult` containing grouped matches. A resolved match includes:

- symbol and company name;
- resolution status;
- statement type and period metadata;
- publication date;
- provider, external company ID, and external statement ID as evidence;
- each requested value with its normalized metric code, original source title when available, and requested clue.

An unresolved identity result has null company display fields and retains provider/statement evidence for diagnostics. A no-match result is deterministic and must not guess a company.

## Acceptance Criteria

1. Given one exact numeric clue in the latest `IncomeStatement`, the service returns the matching listed company and line-item evidence.
2. Given two exact numeric clues present in the same latest statement, the service returns one company/statement result containing evidence for both clues.
3. Given a numeric value and a metric code or persisted source-title clue, the service matches only when both constraints apply to the same line item.
4. Given a statement with null `FinancialStatements.CompanyId` and a valid provider/external-company mapping, the service resolves and returns the eligible company.
5. Given a valid `FinancialStatements.CompanyId` and a conflicting or absent external mapping, the service uses the local company relationship first.
6. Given an exact decimal clue, the service compares database numerics without floating-point conversion, rounding, tolerance, or display-string formatting.
7. Given a value present only in an older statement, the service does not match that company through the older statement.
8. Given requested values distributed across different statements, the service returns no match.
9. Given duplicate normalized/provider source representations for a matched line item, the service returns one company result and one canonical evidence item while retaining duplicate details only diagnostically.
10. Given no latest statement containing all requested clues, the service returns a deterministic value-no-match result without guessing.
11. Given a matching statement that cannot be resolved to an eligible company by either resolution path, the service reports unresolved identity rather than presenting a guessed symbol.
12. Given a metric/title/alias clue without a numeric value, validation rejects the request for the initial implementation.

