# Feature 140 Mapping Revision Report

1. Revision outcome: COMPLETE

2. Files changed

- UserStory.md
- Design.md
- Tasks.md
- SpecReview.md
- README.md
- RevisionReport.md

3. Main architecture change

Feature 140 now includes an explicit identity-resolution layer from IME producer identity to `Companies.Id`.

4. Mapping model

Added conceptual `ImeProducerCompanyMappings` with:

- `ImeProducerCode`
- `ImeProducerName`
- `NormalizedProducerName`
- `CompanyId`
- `MappingStatus`
- `MappingSource`
- `IsActive`
- audit timestamps

5. Trade model

Added derived/link metadata:

- `ImeProducerCode`
- `ResolvedCompanyId` or equivalent deterministic join
- `ProducerMappingId` or equivalent

Raw provider fields remain unchanged.

6. Companies relationship

`Companies.Id` is canonical. `CompanySymbol` is not duplicated as mapping truth.

7. Resolution behavior

- confirmed code mapping first;
- optional exact normalized-name candidate;
- fuzzy similarity does not auto-confirm;
- unmapped rows still ingest;
- conflicts are surfaced;
- historical observations can be re-resolved.

8. Idempotency protection

Mapping metadata is excluded from `SourceFingerprint`.

9. Acceptance criteria count

22

10. Task count

23

11. Scope boundary

Monthly aggregation, prediction, Codal reconciliation, alerts, AI tools, and user-facing analytics remain out of scope.

12. source-fields.json

Unchanged. Mapping metadata is not provider source data.

FEATURE_140_MAPPING_REVISION_COMPLETE
