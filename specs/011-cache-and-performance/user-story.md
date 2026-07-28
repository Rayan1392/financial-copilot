# User Story — Cache and Performance

## Story

As a scanner user,  
I want common scanner queries to return quickly,  
so that the product feels responsive and scalable.

## Acceptance Criteria

- Redis cache is available through abstraction.
- Parsed query plans can be cached.
- Popular scanner results can be cached with freshness policy.
- Cache keys include tenant/client and relevant data version.
- Cache invalidation occurs after data sync/metric recalculation.
- API response includes data freshness metadata.
- AI facade responses can use cached internal Scanner Tool results without exposing tool routing to the React UI.
- Cache hits still pass through `FinancialCopilot.Billing` entitlement, reservation/finalization, and versioned operation-pricing policy; cache use may reduce cost but never bypass accounting.

## Technical Notes

- Do not cache user-specific sensitive data globally.
- Cache deterministic scanner results, not raw LLM messages.
- Cached result freshness and `cached` usage metadata must be returned through the same facade contract as non-cached answers.
