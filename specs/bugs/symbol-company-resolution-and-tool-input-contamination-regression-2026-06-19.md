# Symbol/Company Resolution and Tool-Input Contamination Regression

Status: Root cause confirmed and production fix implemented.

Date: 2026-06-19

## Summary

Two distinct regressions are active in the current backend lookup pipeline:

1. The active V2 direct-metric path passes the enriched conversation prompt into the financial symbol parser.
   That enriched text can contain:
   - `[Recent conversation]`
   - `User`
   - `Assistant`
   - prior answers
   - table text
   - stored context blocks

   Because the parser receives that full enriched string instead of only the latest user message, it can extract a corrupted entity value and forward that corrupted raw symbol/company name into the lookup service.

2. Company-name resolution still has a normalization gap for common spacing and punctuation variants.
   The current normalizer handles:
   - ZWNJ / ZWJ / BOM-style invisible characters
   - Arabic Yeh -> Persian Yeh
   - Arabic Kaf -> Persian Kaf

   It does **not** normalize:
   - internal spacing variants
   - punctuation
   - `گل گهر` vs `گلگهر`
   - other equivalent company-title spellings that differ only by whitespace joining

These two issues interact. Clean exact symbols such as `کگل` can still work, while company-name queries and follow-up lookups fail depending on whether the parser sees clean latest-user input or a polluted enriched prompt.

## Observed Behavior Mapped to Current Code

### Works

- `آخرین فروش کگل`
  - Clean exact symbol.
  - Resolved by `CompanyResolverService` exact symbol fields.

- `آخرین فروش فولاد مبارکه اصفهان`
  - Likely succeeds because the company name as typed matches the stored `Companies.Name` form closely enough for current exact/contains matching.

### Fails

- `آخرین فروش گل گهر`
  - Fails when the stored company title and the user phrase differ only by spacing/joining form, because the current normalizer does not collapse whitespace or punctuation variants.

- `آخرین فروش چادرملو`
  - Can fail when the parser receives an enriched message rather than the latest user message and extracts a polluted symbol/company phrase.
  - The resolver itself already has a fragment fallback for clean `چادرملو`, so this symptom points at tool-input construction or polluted parser input, not only the resolver.

- Follow-up queries such as:
  - `پی به ای چادرملو`
  - `pe کچاد`
  - short company/symbol follow-ups after prior turns

  These fail because the V2 direct lookup path passes `msg.EnrichedMessage` into `SymbolLookupToolAdapter`, and the parser can therefore see full prior conversation text.

## Exact Failing Execution Path

### Path A: Tool-input contamination in active V2 flow

1. `FinancialCopilotWorkflowDefinition.ExecuteConversationAndMemoryStepAsync`
   - Builds `enrichedMessage = BuildEnrichedMessage(msg.Request.Message, memoryContext)`.
   - `BuildEnrichedMessage(...)` prepends `[Recent conversation]` and `[Stored context]` sections when memory items exist.

2. `FinancialCopilotWorkflowDefinition.ExecuteAgentStepAsync`
   - Detects direct metric lookup or direct metric follow-up.
   - Calls:

   ```csharp
   lookupResult = await lookupAdapter.LookupAsync(
       msg.EnrichedMessage,
       request.CorrelationId,
       request.TenantId,
       request.ActorId,
       ct,
       queryTextForLookup: queryTextForLookup);
   ```

   This is the critical bug.
   The first argument to `LookupAsync` is the parser input, and it is the enriched conversation text, not the latest user message.

3. `SymbolLookupToolAdapter.LookupAsync`
   - Creates:

   ```csharp
   var parseRequest = new SymbolLookupParseRequest(
       userQuery,
       "fa",
       correlationId,
       tenantId,
       DateOnly.FromDateTime(now.DateTime));
   ```

   There is no sanitization layer before parser invocation.
   There is also no rejection of:
   - `[Recent conversation]`
   - `User`
   - `Assistant`
   - previous assistant answer text

4. `LlmSymbolLookupParser.ParseAsync`
   - Sends `request.Message` directly to the LLM structured parser.
   - Deterministic fallback only activates when:
     - the LLM returns clarification / no pairs, or
     - all parsed pairs fail metric resolution

   If the LLM returns a pair with a valid metric but a polluted entity extracted from enriched conversation text, the parser accepts it and does **not** fall back to clean direct parsing from the latest user message.

5. `EfCoreSymbolMetricLookupService.LookupAsync`
   - Receives raw parsed symbol names from the parser.
   - Calls `CompanyResolverService.ResolveBySymbolAsync(name, ...)`.
   - When `name` is polluted with `[Recent conversation] ... User ... Assistant ...`, resolution fails and the lookup returns unresolved / empty rows.

### Path B: Company-name normalization gap

1. `CompanyResolverService.ResolveBySymbolAsync`
   - Uses `PersianSymbolNormalizer.Normalize(symbol)`.

2. `PersianSymbolNormalizer.Normalize`
   - Only:
     - trims
     - removes invisible characters
     - maps Arabic Yeh/Kaf to Persian Yeh/Kaf

   It does **not**:
   - collapse internal whitespace
   - remove punctuation
   - normalize joined vs separated words
   - canonicalize `گل گهر` and `گلگهر`

3. `CompanyResolverService`
   - Exact checks compare normalized strings as-is.
   - Fragment fallback uses:

   ```csharp
   c.NormalizedName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
   ```

   Since internal spaces remain significant, the following are not equivalent:
   - `گل گهر`
   - `گلگهر`

   Therefore a stored name like `معدنی و صنعتی گلگهر` will not match input `گل گهر`, and a stored name like `معدنی و صنعتی گل گهر` will not match input `گلگهر`.

## Why the Current Branch Behaves Inconsistently

- Clean exact symbol input works because exact symbol fields still win early in `CompanyResolverService`.
- Long exact company names can work if the stored `Companies.Name` form matches the user wording closely.
- Short company-name queries are more fragile because:
  - they rely on fragment fallback or exact-name normalization
  - the parser can be polluted by enriched memory text
  - there is no validation layer between parser output and financial lookup invocation

This explains why:
- `کگل` works
- `فولاد مبارکه اصفهان` can work
- `گل گهر` fails
- `چادرملو` and `pe کچاد` can fail after follow-up turns

## Confirmed Root Causes

### Root Cause 1

`FinancialCopilotWorkflowDefinition` passes `msg.EnrichedMessage` into `SymbolLookupToolAdapter.LookupAsync` for V2 direct metric lookups.

That means the parser input is not restricted to the latest user request.

### Root Cause 2

`SymbolLookupToolAdapter` has no strict sanitization/validation layer before parser or lookup execution.

There is no guard that rejects parser inputs or extracted entities containing conversation-history markers such as:
- `[Recent conversation]`
- `User`
- `Assistant`
- markdown/table content

### Root Cause 3

`PersianSymbolNormalizer` is too weak for company-name equivalence matching.

It does not normalize:
- internal spaces
- punctuation
- joined vs separated forms like `گل گهر` / `گلگهر`

### Root Cause 4

Existing resolver tests cover:
- exact symbol fields
- Yeh/Kaf normalization
- `چادرملو` fragment fallback

But they do **not** cover:
- `گل گهر`
- `گلگهر`
- punctuation/spacing normalization
- polluted parser/entity input from enriched conversation text

## Non-Root Causes

These parts are not the primary defect:

- `EfCoreSymbolMetricLookupService` already resolves via `CompanyResolverService` and then uses `ExternalCompanyId` for lookup.
- `Companies` is already the active source of truth for resolution in the lookup pipeline.
- The monthly-sales deterministic parser fallback exists for clean direct monthly-sales phrasing.

The failure is happening before or at entity resolution quality, not after successful `ExternalCompanyId` resolution.

## Affected Files / Classes

- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
  - direct metric preflight passes enriched prompt into lookup parser path

- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Adapters/SymbolLookupToolAdapter.cs`
  - no sanitization or validation before parser invocation
  - no fallback to latest-user-only extraction when history markers are present

- `src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs`
  - trusts incoming parser input
  - deterministic fallback does not protect against polluted but metric-resolvable LLM entity extraction

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CompanyResolverService.cs`
  - exact + fragment matching is present, but company-name normalization is not strong enough for spacing/punctuation variants

- `src/backend/FinancialCopilot.Domain/Financial/Services/PersianSymbolNormalizer.cs`
  - insufficient normalization for company-title equivalence

## Required Regression Tests

### Tool-input contamination

- Follow-up PE query where prior conversation exists:
  - latest user message: `pe کچاد`
  - prove the tool entity is exactly `کچاد`
  - prove parser/lookup input does not contain `[Recent conversation]`, `User`, `Assistant`, or previous answer text

- Follow-up company-name query:
  - latest user message: `پی به ای چادرملو`
  - prove entity passed to lookup is exactly `چادرملو`

### Company-name resolution

- `آخرین فروش کگل` -> resolves to `کگل`
- `آخرین فروش گل گهر` -> resolves to `کگل`
- `آخرین فروش گلگهر` -> resolves to `کگل`
- `آخرین فروش چادرملو` -> resolves to `کچاد`
- `پی به ای چادرملو` -> resolves to `کچاد`
- `آخرین فروش فولاد مبارکه اصفهان` -> resolves to `فولاد`

### Normalization coverage

- unit tests for `PersianSymbolNormalizer`:
  - `گل گهر` == `گلگهر`
  - punctuation-insensitive matching where intended
  - space-collapse behavior

- unit tests for `CompanyResolverService`:
  - exact `CompanySymbol`
  - exact `Name`
  - safe fragment fallback
  - `گل گهر` / `گلگهر` equivalence
  - punctuation-insensitive company-title matching

## Implemented Fix

The fix was implemented in the backend lookup pipeline, not in prompt examples:

1. `FinancialCopilotWorkflowDefinition` no longer passes the enriched conversation prompt as the parser input for direct metric lookups.
   - Latest user message is now passed as the primary lookup-parser input.
   - Enriched conversation text is still available separately as parser context for safe follow-up reconstruction.

2. `SymbolLookupToolAdapter` now applies a strict sanitization / validation layer before parser and lookup execution.
   - It extracts the latest user message from any enriched prompt.
   - It strips `[Recent conversation]`, `[Stored context]`, `User`, `Assistant`, and stored-context bullet lines from parser context.
   - For entity-only follow-ups such as `چادرملو`, it reconstructs a safe parser input from the previous user metric turn plus the latest entity.
   - For metric-only follow-ups such as `فروش YTD چقدر بوده؟`, it reconstructs a safe parser input from the previous user symbol turn plus the latest metric question.
   - If the parsed entity still contains conversation-history markers or structured noise, the adapter rejects it before lookup execution.

3. `PersianSymbolNormalizer` was strengthened so company-name comparisons ignore spacing and punctuation variants.
   - This makes `گل گهر` and `گلگهر` equivalent.
   - It also makes punctuation variants such as `فولاد-مبارکه، اصفهان` resolve against `فولاد مبارکه اصفهان`.

4. `CompanyResolverService` now benefits from the stronger shared normalizer without introducing a parallel resolution path.

## Validation Evidence

Focused validation passed after the implementation:

- `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release -m:1 --no-restore --filter "CompanyResolverServiceTests|PersianSymbolNormalizerTests|SymbolLookupParserTests|SymbolLookupToolAdapterTests"`
  - Passed: `71`

- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release -m:1 --no-restore --filter "FullyQualifiedName~FinancialCopilot.IntegrationTests.V2SymbolLookupEndpointTests|FullyQualifiedName~FinancialCopilot.IntegrationTests.V2MonthlySalesRoutingEndpointTests"`
  - Passed: `17`

Validated regressions include:

- `آخرین فروش گل گهر` -> resolves to `کگل`
- `آخرین فروش گلگهر` -> resolves to `کگل`
- `آخرین فروش چادرملو` -> resolves to `کچاد`
- `پی به ای گل گهر` -> resolves to `کگل`
- `پی به ای گلگهر` -> resolves to `کگل`
- explicit follow-up `pe کچاد` uses the latest user message as parser input and no longer leaks `[Recent conversation]`, `User`, or `Assistant`

## Fix Direction

The correct fix is in the backend resolution pipeline, not in prompt examples:

1. Parser input for financial lookup tools must be derived from the latest user message, not the enriched conversation prompt.
2. Add strict entity sanitization/validation before financial lookup invocation.
3. If contamination markers are present, reject and re-extract only from the latest user message.
4. Strengthen company-name normalization in the resolver path using `Companies` as source of truth.
5. Continue using resolved company identity (`ExternalCompanyId` / resolved symbol) for all downstream metric lookups.

Do **not** solve this only by adding hard-coded prompt examples.

## Evidence References

- V2 enriched prompt construction: `FinancialCopilotWorkflowDefinition.BuildEnrichedMessage`
- V2 direct metric lookup invocation: `FinancialCopilotWorkflowDefinition.ExecuteAgentStepAsync`
- parser entry point: `SymbolLookupToolAdapter.LookupAsync`
- parser deterministic fallback boundary: `LlmSymbolLookupParser.BuildParseResult`
- company resolution path: `CompanyResolverService.ResolveBySymbolAsync`
- current normalization scope: `PersianSymbolNormalizer.Normalize`
