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

## Technical Notes

- Do not cache user-specific sensitive data globally.
- Cache deterministic scanner results, not raw LLM messages.
