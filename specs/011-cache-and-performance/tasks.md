# Tasks

- Implement cache abstraction.
- Add Redis implementation.
- Add scanner plan cache.
- Add scanner result cache.
- Integrate cached scanner execution behind the AI Query Orchestrator.
- Integrate cache-hit facts with `IUsageChargeCalculator` so cached-response pricing is committed through Billing.
- Add cache invalidation event.
- Add integration tests with fake cache.
- Add integration tests proving cache hits return freshness/cache metadata and produce one policy-defined Billing ledger outcome.
