# Feature 140 — Product and Architecture Self-Review

## Verdict

**READY_FOR_IMPLEMENTATION_AFTER_REPOSITORY_DISCOVERY_AND_REVIEW_GATE**

## Major revised decision

Feature 140 now includes IME producer → FinancialCopilot company mapping.

This is justified because the stated product purpose is company-level commodity-exchange analysis and later monthly-sales estimation. Without identity resolution, raw ingestion alone cannot support deterministic company aggregation.

## Key architecture decision

Do NOT put mapping semantics into the provider-owned fields.

Use:

```text
IME Symbol / ProducerName
        ->
ImeProducerCode
        ->
ImeProducerCompanyMappings
        ->
Companies.Id
```

The `Companies` table remains canonical for company identity.

## Why mapping targets Companies.Id

Mapping directly to a copied symbol string is fragile.

`Companies.Id` provides stable relational identity while `CompanySymbol`, `TseSymbol`, `Name`, and instrument-related columns remain attributes of the company record.

## Risks and mitigations

| Risk | Severity | Mitigation |
|---|---:|---|
| Same IME prefix mapped to two companies | High | active mapping uniqueness + conflict state |
| Producer name spelling differences | Medium | normalized alias/candidate handling, no blind fuzzy confirmation |
| Raw IME data contaminated by mapping | High | preserve provider fields; mapping is additive metadata |
| Company row duplicates/provider variants | High | repository discovery + explicit Companies.Id selection |
| Mapping changes break idempotency | High | exclude mapping metadata from SourceFingerprint |
| Historical rows remain unmapped | Medium | re-resolution operation |
| Prefix assumption is wrong for some symbols | Medium | explicit mapping required; malformed/unknown stays unresolved |
| Auto-mapping wrong company | High | confirmed mapping required; candidates are not truth |
| Company symbol changes | Medium | FK to Companies.Id, not copied symbol truth |

## Scope check

Still excluded:

- monthly aggregation;
- sales forecasting;
- Codal comparison;
- alerts;
- AI/LLM tools;
- user-facing analytical answers.

## Review gate

Implementation should proceed only after repository discovery confirms:

1. actual `Companies` entity and PK type;
2. existing company uniqueness/provider semantics;
3. whether `CompanySymbol` can have duplicates;
4. existing Admin mapping patterns;
5. migration/index conventions.
