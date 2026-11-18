# BUG-033: AI Query Reservation and Symbol-Grounded Fallback

Status: Gate B: Approved by the owner on 2026-11-18

## Discovery

Read-only production inspection on 2026-11-18 found the API, Worker, and Frontend containers running on `prod` commit `3c68b8f2` (`v1.17.0`). No server state was changed.

The API logged seven `ArgumentOutOfRangeException` failures from 10:11:40Z through 10:23:08Z. The Report Discussion turn at 10:18:49Z was `Answered`, with 30 report facts, 21 checked numbers, and zero ungrounded numbers, then failed during billing finalization. A separate financial-statement request at 10:18:33Z selected `financial_statement_table` and resolved the company. The logs do not establish that these two correlations belong to the same user conversation.

The billing mismatch is reproducible from source. `AiQueryBillingOperationSelector` maps financial-statement wording to `AiQuery.FinancialStatement`, priced at 2 credits. `ReportDiscussionAnswered` currently applies a 2x completion multiplier, so that operation's final charge is 4 credits. The Report Discussion pre-call capacity callback only ensures `ReportDiscussionBillingPolicy.AnsweredCredits` (2 credits). If the original 2-credit reservation is already sufficient for the callback, the callback does not increase it, and finalization rejects the 4-credit charge. This contradicts Feature 162's fixed 2-credit contract for every answered Report Discussion family. Existing billing tests covered a one-credit base operation but not a financial-statement base operation.

Feature 163 is already included in the inspected production release (`v1.17.0`), and `SymbolGroundedFallback:Enabled` is true in the API container. Its approved contract replaces `Unsupported` turns (and the specific “no recent analysis” outcome) only when one exact company and a usable quarterly income statement are available; other `NoData` outcomes remain unchanged.

Further read-only inspection found that the persisted initial answer has `OutcomeReasonCode=entity_not_found` and a null `FinancialStatementTableResult`. The resolved company has 15 quarterly income-statement rows in the production database (latest period end 2026-06-21), but those rows are stored as `ProviderName=CyclicalWaves`. The current normalized statement-table repository filters both `FindLatestStatementAsync` and `GetQuarterlyIncomeStatementReportsAsync` to `NoavaranCurrentApi`. The Feature 163 handler uses that same statement-table use case, and its current eligibility excludes `EntityNotFound`. Therefore the rows observed in the other provider do not establish an eligible report for the active query path, and an implicit provider fallback would violate the documented provider-isolation policy. The first response's “company not found” reason is misleading, but making it answer from the other provider requires a separately approved source-routing change.

## Approved Correction

1. Preserve Feature 162's fixed 2-credit price for every successful Report Discussion answer, regardless of the base operation selected for the user's wording. Keep measured usage-based charging in Enforced mode and preserve zero-charge statuses.
2. Add a billing regression test for `AiQuery.FinancialStatement` plus `ReportDiscussionAnswered`, proving settlement charges exactly 2 credits and releases the unused hold.
3. Verify the downstream company and quarterly-report lookup for the initial request. Do not change Feature 163's supported `NoData` policy or cross provider boundaries without renewed design approval.
4. Keep production deployment and feature-flag changes out of scope.

## Risks and Constraints

- Preserve the domain invariant that a committed charge cannot exceed its reserved capacity; do not suppress the exception or silently clamp the charge.
- Preserve usage-based Enforced billing, zero-charge outcomes, and insufficient-credit behavior.
- No schema migration is expected.
- The production root credential was provided in chat; rotate it before future use.

## Implementation and Verification

- `AiFacadeBillingHook` now applies the fixed answered-discussion price whenever the operation is not actually covered by usage-based Enforced billing. Enforced operations continue to settle from measured usage.
- Added an integration regression for `AiQuery.FinancialStatement` settling `ReportDiscussionAnswered` at exactly 2 credits, and a regression proving covered Enforced operations still use measured usage.
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~UsageBasedCreditBilling|FullyQualifiedName~ReportDiscussion162EndpointTests|FullyQualifiedName~SymbolGroundedFallback163EndpointTests"`: 35 passed, 7 PostgreSQL tests skipped because their database configuration was unavailable.
- The focused build and the related Report Discussion/Feature 163 integration paths passed. `git diff --check` passed.
- The initial no-result response is now traced to a provider mismatch: production has quarterly income-statement rows for the resolved company under `CyclicalWaves`, while the active statement-table query filters to `NoavaranCurrentApi`. Feature 163 remains unchanged because those rows are not available through its configured query path and its approved contract does not replace `EntityNotFound`/supported `NoData` results. A proposed source-routing design is recorded at `specs/163-symbol-grounded-fallback-answer/changes/financial-statement-source-routing/design-proposal.md`; it is not approved or implemented.
- No production deployment or configuration change was made.
