# Feature 131 — Financial Statement Value Search

## 1. Goal

Allow an operator or user-facing workflow to identify a listed company from one or more exact numeric values found in its latest financial statement.

For example, given two income-statement values—`2,580,407` and `3,300,508`—the system should search persisted statement line items, identify the company that contains both values in the same latest statement, and return the symbol, company name, item titles, normalized metric codes, and statement period. The user may also provide the known title or meaning of an item, such as `فروش خالص و درآمد ارائه خدمات`, `درآمد`, or a governed alias for that item.

This feature is intended for investigation, data-quality checks, support workflows, and future natural-language questions such as:

- “Find the symbol whose latest income statement contains 2,580,407.”
- “The symbol has gross profit of 2,580,407 and net sales and service revenue of 3,300,508; which symbol is it?”
- “Which symbol has `فروش خالص و درآمد ارائه خدمات` equal to 3,300,508 in its latest income statement?”

## 2. Problem and Root Cause

Financial statements and their line items are persisted separately:

- `FinancialStatements` stores the statement header and provider identifiers.
- `FinancialStatementLineItems` stores numeric values and normalized `MetricCode` values.
- `FinancialStatementSourceItems` stores provider-facing item titles.
- `NoavaranEligibleCompanies` provides the listed-company symbol catalog and provider external-company mapping.

Some current-provider statements do not have a populated local `FinancialStatements.CompanyId`. A query that relies only on `CompanyId` therefore misses valid statements even though the statement and line items are present.

The authoritative linkage for this flow must support:

```text
FinancialStatements.ProviderName
  + FinancialStatements.ExternalCompanyId
    → NoavaranEligibleCompanies.ProviderName
      + NoavaranEligibleCompanies.ExternalCompanyId
        → CompanySymbol / TseSymbol / Name
```

The search must not depend exclusively on `FinancialStatements.CompanyId`.

## 3. Scope

### In scope

- Search persisted, normalized financial-statement data by exact numeric value.
- Accept an optional source-item title, normalized metric code, or governed semantic alias as an additional clue.
- Restrict the default search to the latest relevant statement per eligible company.
- Support `IncomeStatement` as the initial statement type.
- Accept one or multiple numeric clues.
- Return only companies whose latest matching statement contains every requested clue.
- Resolve symbols through the provider/external-company mapping when the local company foreign key is absent.
- Return the matched line-item title, normalized metric code, value, provider, statement period, publication date, and external statement identifier.
- Preserve duplicate source representations without returning duplicate company results.
- Execute as a read-only query against the configured application database; no production credentials or provider secrets are embedded in code.

### Out of scope

- Fuzzy numeric matching, rounding-based matching, or currency conversion during search.
- Searching raw provider APIs at query time.
- Modifying or repairing financial-statement data.
- Inferring a company from unrelated statement types unless explicitly requested.
- Treating similar-sounding financial concepts as interchangeable without an explicit governed catalog mapping.
- Exposing database connection details in logs or user-facing responses.
- Replacing the existing full financial-statement query feature (`083`).

## 4. Functional Requirements

### 4.0 Initial integration boundary

Feature 131 uses **B) Application service only for an operator/internal workflow**.
The initial implementation does not add a public API route and is not invoked from the
AI query facade. An existing internal operator/support workflow may invoke the application
service directly; exposing that workflow is outside this feature.

The Application contract is:

```csharp
public interface IFinancialStatementValueSearchService
{
    Task<FinancialStatementValueSearchResult> SearchAsync(
        FinancialStatementValueSearchRequest request,
        CancellationToken cancellationToken = default);
}

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

`Clues` must contain at least one numeric clue. Each clue may have at most one optional
metric/title/alias refinement, and the numeric value is always mandatory. The default
statement type is `IncomeStatement`; the provider is the configured financial-statement
provider. The service returns the result shape in section 4.5, including grouped statement
evidence and a deterministic distinction between value no-match and unresolved company
identity. No AI parser, public endpoint, or new identity service is part of the initial target.

### 4.1 Input

The application service accepts:

```text
statementType     = IncomeStatement (default for this feature)
values            = one or more exact decimal values
periodPolicy      = latest relevant statement (default)
providerPolicy    = configured financial-statement provider
```

Values must be parsed using an invariant numeric representation after normalizing common localized digits and separators. The parsed value must retain decimal precision; the implementation must not convert financial values to floating-point types for comparison.

The optional item clue may be supplied as:

- a normalized metric code, for example `REVENUE` or `GROSS_PROFIT`;
- the persisted Persian source title, for example `فروش خالص و درآمد ارائه خدمات`;
- a governed Persian alias, for example `فروش خالص`, `درآمد فروش`, or `درآمد` when the semantic catalog explicitly maps that alias to `REVENUE`.

Item-title matching must use the existing governed financial semantic layer/catalog and persisted source-item mapping. It must not use unrestricted substring matching or LLM-generated SQL. When a source title is available, preserve that exact title in the evidence returned to the caller.

### 4.2 Latest-statement policy

For each eligible company, first determine its latest statement matching the requested statement type and configured provider. “Latest” is ordered by:

1. `PeriodEnd` descending;
2. `PublishedAt` descending, with nulls last;
3. `LastSynchronizedAt` descending;
4. deterministic statement-id or external-statement-id tie-breaker.

All requested values must be evaluated against the same selected statement. A company must not match by combining one value from an older statement with another value from a newer statement.

### 4.3 Exact line-item matching

Match values using the database numeric value exactly:

```sql
FinancialStatementLineItems.Value = requestedValue
```

The query must not use text formatting, locale-specific display strings, approximate tolerance, or rounded comparisons.

For multiple requested values:

- assign each value/clue pair a request-clue identifier;
- require every request-clue identifier to match a line item in the statement;
- return one company/statement result;
- include all matching line items for traceability.

For a value plus an item clue, both constraints must apply to the same line item or to an explicitly governed equivalent mapping:

```text
lineItem.Value = requestedValue
AND lineItem matches requestedMetricCode/title/alias
```

If the caller supplies multiple value/title pairs, each pair must match a line item in the same selected statement. A title, metric code, or alias without a numeric value is invalid for the initial implementation.

The terms `فروش خالص و درآمد ارائه خدمات` / `درآمد` and `سود عملیاتی` are not automatically equivalent. The first normally maps to revenue (`REVENUE` or `TOTAL_REVENUE`), while the second maps to operating profit (`OPERATING_PROFIT`) when that catalog mapping exists. The resolver must return no match or request clarification when the requested title conflicts with the persisted metric mapping; it must never silently relabel revenue as operating profit.

If the same value is supplied more than once, treat it as one clue.

### 4.4 Company resolution

The result must resolve a user-facing symbol using the strongest available linkage:

1. `FinancialStatements.CompanyId` when it references a valid company;
2. `ProviderName + ExternalCompanyId` to `NoavaranEligibleCompanies` for current-provider statements;
3. the equivalent existing provider-neutral external-company mapping for other configured providers;
4. unresolved result when neither the local relationship nor an external mapping resolves.

Only eligible listed companies should be returned when an eligibility catalog exists. Unresolved statements may be retained for diagnostics but must not be presented as a confidently identified symbol.

### 4.5 Result shape

The application result should contain:

```json
{
  "matches": [
    {
      "symbol": "داترا",
      "companyName": "آترا زیست آرای",
      "statementType": "IncomeStatement",
      "periodType": "ThreeMonths",
      "periodStart": "2026-03-21",
      "periodEnd": "2026-06-21",
      "publishedAt": "2026-07-22",
      "providerName": "NoavaranCurrentApi",
      "externalCompanyId": "15624",
      "externalStatementId": "548219",
      "resolutionStatus": "LocalCompanyId | ProviderExternalMapping | Unresolved",
      "items": [
        {
          "value": 2580407,
          "metricCode": "GROSS_PROFIT",
          "sourceTitleFa": "سود ناخالص",
          "requestedClue": "سود ناخالص"
        },
        {
          "value": 3300508,
          "metricCode": "REVENUE",
          "sourceTitleFa": "فروش خالص و درآمد ارائه خدمات",
          "requestedClue": "فروش خالص و درآمد ارائه خدمات"
        }
      ]
    }
  ]
}
```

The exact property names may follow the project’s existing Application contract conventions, but the
result must preserve both the normalized metric identity and the original Persian source title when
available. For an unresolved identity result, company display fields are null and provider and
external-statement evidence remain available for diagnostics.

### 4.6 No-match behavior

Return a deterministic no-match result when:

- no latest statement contains the requested value(s);
- the values occur across different statements for a company;
- the statement exists but cannot be mapped to an eligible symbol.

The response should distinguish “no statement value match” from “statement matched but company identity could not be resolved.” It must not guess a symbol from approximate values or unrelated records.

## 5. Query Design

The logical query is:

```text
configured provider
  → latest statement per resolved company and statement type
  → exact value + same-line-item governed clue match
  → optional governed item-title/metric-alias match
  → require every request clue in the same statement
  → return resolved symbol/name or an unresolved identity result
  → return grouped statement and item evidence
```

The implementation may use a window function, `DISTINCT ON`, or an equivalent EF Core query, provided the ordering and same-statement requirements remain deterministic.

Company resolution is performed before latest-statement partitioning. The query first joins a
valid `FinancialStatements.CompanyId` to `Companies`; only when that relationship is absent or
invalid does it use the existing `ProviderName + ExternalCompanyId` mapping. The resolved company
key is then used to select one latest statement. An unresolved statement may be retained for the
diagnostic outcome, but is never presented as a confidently identified listed company.

Conceptual SQL:

```sql
WITH company_resolution AS (
    SELECT s.*,
           c."Id" AS "LocalCompanyId",
           COALESCE(c."Id", eligible."Id") AS "ResolvedCompanyId",
           COALESCE(c."Ticker", eligible."Ticker", eligible."TseSymbol") AS "ResolvedSymbol",
           COALESCE(c."Name", eligible."Name") AS "ResolvedCompanyName",
           CASE WHEN c."Id" IS NOT NULL THEN 'LocalCompanyId'
                WHEN eligible."Id" IS NOT NULL THEN 'ProviderExternalMapping'
                ELSE 'Unresolved' END AS "ResolutionStatus"
    FROM "FinancialStatements" s
    LEFT JOIN "Companies" c ON c."Id" = s."CompanyId"
    LEFT JOIN "NoavaranEligibleCompanies" eligible
      ON eligible."ProviderName" = s."ProviderName"
     AND eligible."ExternalCompanyId" = s."ExternalCompanyId"
    WHERE s."ProviderName" = :configured_provider
      AND s."StatementType" = :statement_type
), latest_statements AS (
    SELECT s.*,
           ROW_NUMBER() OVER (
               PARTITION BY COALESCE(s."ResolvedCompanyId"::text,
                                     s."ProviderName" || ':' || s."ExternalCompanyId")
               ORDER BY s."PeriodEnd" DESC,
                        s."PublishedAt" DESC NULLS LAST,
                        s."LastSynchronizedAt" DESC,
                        s."Id" DESC
           ) AS row_number
    FROM company_resolution s
), matching_statements AS (
    SELECT li."FinancialStatementId", rc."ClueId"
    FROM latest_statements s
    JOIN "FinancialStatementLineItems" li
      ON li."FinancialStatementId" = s."Id"
    JOIN :resolved_requested_clues rc
      ON li."Value" = rc."Value"
     AND (rc."MetricCodes" IS NULL OR li."MetricCode" = ANY(rc."MetricCodes"))
     AND (rc."SourceItemIds" IS NULL OR li."SourceItemCatalogId" = ANY(rc."SourceItemIds"))
    WHERE s.row_number = 1
      AND li."Value" = ANY(:requested_values)
    GROUP BY li."FinancialStatementId", rc."ClueId"
), qualifying_statements AS (
    SELECT "FinancialStatementId"
    FROM matching_statements
    GROUP BY "FinancialStatementId"
    HAVING COUNT(DISTINCT "ClueId") = :distinct_clue_count
)
SELECT ...
FROM latest_statements s
JOIN qualifying_statements m ON m."FinancialStatementId" = s."Id"
JOIN "FinancialStatementLineItems" li
  ON li."FinancialStatementId" = s."Id"
LEFT JOIN "FinancialStatementSourceItems" source_item
  ON source_item."Id" = li."SourceItemCatalogId"
LEFT JOIN "NoavaranEligibleCompanies" eligible
  ON eligible."ProviderName" = s."ProviderName"
 AND eligible."ExternalCompanyId" = s."ExternalCompanyId"
JOIN matching_statements matched_clue
  ON matched_clue."FinancialStatementId" = s."Id"
JOIN :resolved_requested_clues rc
  ON rc."ClueId" = matched_clue."ClueId"
 AND rc."Value" = li."Value"
 AND (rc."MetricCodes" IS NULL OR li."MetricCode" = ANY(rc."MetricCodes"))
 AND (rc."SourceItemIds" IS NULL OR li."SourceItemCatalogId" = ANY(rc."SourceItemIds"))
WHERE li."Value" = ANY(:requested_values)
ORDER BY s."PeriodEnd" DESC, eligible."CompanySymbol", li."Id";
```

The conceptual `:resolved_requested_clues` parameter contains one row per distinct value/clue
pair, including a `ClueId`, exact decimal `Value`, and the already-resolved governed metric codes
and/or source-item identifiers. The value and clue predicates are therefore applied to the same
line-item row. `qualifying_statements` requires every clue, not merely every distinct numeric value,
to match inside the same latest statement.

Duplicate source representations are rows for the same statement, clue, exact value, normalized
metric identity, and source-item/provider-code identity that differ only because normalization and
provider-code ingestion both persisted them. Select one canonical evidence row deterministically:
populated normalized `MetricCode` first, then populated `SourceItemCatalogId`, then lowest line-item
`Id`. Return one company/statement result and one canonical item entry; retain duplicate row IDs and
source titles only in internal diagnostic evidence.

When an item title is supplied, resolve it to governed metric codes and/or source-item identifiers before executing the value predicate. The final query must constrain both value and resolved item identity. If a title resolves to multiple canonical representations (for example `REVENUE` and `TOTAL_REVENUE`), they may be treated as equivalent for matching, but the response must expose the actual persisted metric code and source title.

## 6. Invocation Boundary

The initial implementation is invoked only by the internal operator/support workflow through
`IFinancialStatementValueSearchService`. There is no AI-facade intent, parser, public API route,
or general screener integration in Feature 131. Any later caller must translate its input into the
application contract in section 4.0 and remain subject to the same deterministic query rules.




## 7. Security and Operational Requirements

- Use the configured application connection string and provider settings.
- Use a read-only database transaction/connection where the deployment supports it.
- Never log connection strings, passwords, API keys, or full financial payloads.
- Log only correlation ID, normalized statement type, number of clues, execution duration, match count, and failure category.
- Apply bounded limits to the number of input values and returned matches/items.
- Parameterize all values and filters; do not concatenate user input into SQL.

## 8. Acceptance Criteria

1. Searching the latest `IncomeStatement` for `2,580,407` identifies `داترا` when the matching statement has no usable `CompanyId` but has the provider/external-company mapping.
2. Searching the same statement for both `2,580,407` and `3,300,508` returns `داترا` exactly once.
3. The result identifies `2,580,407` as `GROSS_PROFIT` / `سود ناخالص`.
4. The result identifies `3,300,508` as `REVENUE` / `فروش خالص و درآمد ارائه خدمات` and preserves any equivalent normalized mapping such as `TOTAL_REVENUE` without duplicating the company result.
5. A query for `فروش خالص و درآمد ارائه خدمات = 3,300,508` matches the revenue line item in the same latest statement.
6. A governed alias such as `درآمد` resolves to the configured revenue metric and can be combined with an exact value.
7. A query for `سود عملیاتی = 3,300,508` does not match a revenue row unless the financial semantic catalog explicitly defines that equivalence.
8. A value present only in an older statement does not cause the latest statement to match.
9. Two values split across different statements do not produce a match.
10. Values are compared exactly as database numerics, including decimal values.
11. Provider and external-company identifiers are included in internal evidence, while secrets are never exposed.
12. An unresolved company is reported as unresolved rather than guessed.
13. Unit tests cover localized numeric parsing, title/alias resolution, latest-statement selection, null `CompanyId` fallback linkage, same-statement multi-value matching, duplicate source mappings, conflicting revenue/operating-profit titles, and no-match behavior.

## 9. Verification Example

For the observed production data, the expected evidence is:

```text
Symbol: داترا
Company: آترا زیست آرای
Statement: IncomeStatement / ThreeMonths
Period: 2026-03-21 through 2026-06-21
Published: 2026-07-22

2,580,407  → سود ناخالص → GROSS_PROFIT
3,300,508  → فروش خالص و درآمد ارائه خدمات → REVENUE
```

This example is a verification fixture for the search behavior, not a hardcoded production result. Future data refreshes may change the latest statement or the matching company.
