# Proposed: Symbol-Grounded Fallback for Provider-Scoped Income Statements

Status: Proposed — not approved

## Discovery

BUG-033 production evidence shows an initial «سود و زیان غبهنوش را تفسیر کن» turn persisted with `entity_not_found` and no `FinancialStatementTableResult`. The canonical company exists and has 15 quarterly income-statement rows, but the rows are stored under `ProviderName=CyclicalWaves`. The normalized statement-table repository currently filters quarterly and latest-statement reads to `NoavaranCurrentApi`.

Feature 163 is enabled in production. Its current eligibility replaces `Unsupported` turns and the specific “no recent analysis” outcome, then requires one exact company and a non-empty quarterly income-statement result from `IFinancialStatementTableQueryUseCase`. It does not replace `EntityNotFound` or other supported `NoData` outcomes. Consequently, Feature 163 cannot use the observed CyclicalWaves rows through the current statement-table path.

The project documents that chat data does not fall back across providers. Reading rows from another provider implicitly would weaken that boundary and could expose stale or differently scoped data.

## Proposed Goal

Allow a first-turn Persian interpretation request to use Feature 163 only when the company is resolved exactly and a quarterly income statement is available from the explicitly selected source for that capability. Keep implicit cross-provider fallback disabled. If the selected source has no usable report, retain the existing no-data behavior.

## Proposed Design

1. Extend provider-source selection for `financial_statement_table` only through an explicit, validated capability-source mapping. Do not infer a source from rows that happen to exist in storage.
2. Make the selected source flow through company resolution, report selection, and statement reads as one provider-scoped query. Company identity, external company ID, statement provider, and report rows must remain paired; ambiguous or mismatched identities fail closed.
3. Allow Feature 163 to retry an `EntityNotFound` turn only when its existing exact single-company resolution and non-empty report checks succeed against that same selected source. Keep all existing supported `NoData` exclusions unchanged.
4. Keep the default mapping unchanged. No production flag or source-map change is part of implementation approval; selecting a production source requires its own deployment/configuration authorization after the implementation is reviewed.

## Alternatives

- **Keep current provider boundaries and behavior:** safest and smallest; does not answer the observed first-turn request from the CyclicalWaves rows.
- **Read any provider with a matching external company ID:** rejected because it creates implicit cross-provider fallback and risks source contamination.
- **Change the global chat provider:** broader than this issue and changes unrelated capabilities; not recommended.

## Acceptance Criteria

- With an explicit supported source mapping and a matching quarterly statement, a first-turn exact-symbol interpretation request can produce a Feature 163 grounded answer and activate Report Discussion context.
- With no mapping, an unsupported provider, an ambiguous company, mismatched company/provider identity, or no usable statement, the existing response is preserved and no model call is made.
- Existing `Unsupported` fallback behavior and all current `NoData` exclusions remain unchanged.
- Financial-statement rows are never read from a provider other than the explicit source selected for the capability.
- Add unit and integration regressions for the mapped-source success, unmapped-source no-data case, and provider-isolation case; run the Feature 163, Feature 162, and billing regression suites.

## Expected Areas and Risks

- `FinancialData:CapabilitySources` validation and provider registration.
- Quarterly statement query contracts, company resolution, and normalized repositories.
- Feature 163 eligibility and handler tests.
- Risk: source selection may require a provider adapter for the currently stored CyclicalWaves rows; the legacy `CyclicalWaves` provider is retired, so its rows must not be treated as a supported active source without confirming the intended product source.
- No schema migration is expected from the current evidence. No production deployment or configuration change is included.

## Gate A Review

This proposal is a material extension beyond approved BUG-033 scope because it changes provider-scoped statement routing and the set of outcomes Feature 163 may replace. It requires explicit design approval before final user-story/tasks and a separate implementation-plan approval before production code changes.
