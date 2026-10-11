# Feature 140 — IME Daily Trades Ingestion + Company Mapping

This specification package covers:

- daily IME raw trade ingestion;
- immutable raw observation persistence;
- producer-code extraction from IME symbols;
- deterministic IME producer → `Companies.Id` mapping;
- unresolved/conflict handling;
- Admin mapping operations;
- historical re-resolution.

## Key mapping rule

```text
IME producer identity -> Companies.Id
```

Do not treat `CompanySymbol` as the canonical stored mapping target. It is read from `Companies`.

## Still out of scope

- monthly aggregation;
- forecasting;
- Codal reconciliation;
- alerts;
- AI tools;
- user-facing IME analytics.
