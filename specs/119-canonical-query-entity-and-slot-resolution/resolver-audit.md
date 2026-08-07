# Resolver and Extractor Audit

## Canonical identity authority

`NormalizedCompanyRow` in the Financial Ingestion database is the query-time canonical company identity source. It contains the normalized company name, ticker, TSE symbol, company symbol, English/pinglish aliases, and stable company ID. Financial-data providers remain ingestion sources only; no query-time resolver calls a provider.

## Existing route risks

| Area | Existing behavior | Migration target |
|---|---|---|
| V1 orchestration | Direct lookup and monthly-trend branches pass route-local symbol hints to `ICompanyResolverService`. | `ICanonicalCompanyRouteAdapter` plus `ICanonicalQueryEntityResolver`. |
| Native V2 workflow | Deterministic preflight routes use `*IntentRules.ExtractCompanySymbol`; monthly trend is token-first after phrase stripping. | Resolve `QueryInterpretation.EntityMentions` before route execution. |
| Scanner/lookup services | Call `ICompanyResolverService.ResolveBySymbolAsync` and receive `null` for missing, unknown, and ambiguous results. | Typed `EntityResolutionResult`. |
| Statement/product/P/S use cases | Resolve a single company hint through the legacy nullable resolver. | Staged adapter with feature flag, then Feature 122 migration. |

`Clarification` and `Unknown` are dialogue outcomes, not entity states. The new boundary separates `Missing`, `NotFound`, `Ambiguous`, and `Resolved` before data availability is evaluated.
