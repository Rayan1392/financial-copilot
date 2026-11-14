# T-R00 routing baseline

The owner reviewed and approved the Phase 0 evidence before Phase 1 on 2026-11-14. This approval does not change the verification status of proposed mapping items.

The snapshot was recorded on unchanged application routing code before Feature 161 was implemented. Tests use fake models, isolated in-memory stores and a synthetic statement-table use case. They set `CashFlowReport:Enabled=false`; the option is not implemented yet.

- `routing-baseline.json`: 67 phrases covering the routing matrix and U1-U7, with current V1/V2 rule and interpreter signatures and statement-table query builders.
- `followup-baseline.json`: six independently seeded Feature 083 follow-ups and row 46 with a Feature 154 marker through `ConversationDialogueGate`.
- `endpoint-routing-baseline.json`: V2 endpoint results for all 67 phrases in fresh conversations and U1-U7 in one conversation, plus the exact `FinancialStatementTableQuery` objects sent to the table use case. All 74 responses were HTTP 200. The synthetic table use case returns a successful result to exercise state carry-over.
- `balance-followup-endpoint-baseline.json`: row 46 through the endpoint following a Feature 154 balance-sheet response.
- `v1-endpoint-routing-baseline.json`: V1 endpoint results for all 67 fresh-conversation phrases, including table-use-case requests where invoked.

For non-table routes the endpoint snapshots record response intent and clarification state, not internal request objects of other capabilities. `Executor` is an optional observer result, not a complete dispatcher trace. Models are scripted, so these snapshots do not characterize arbitrary model proposals. U1-U7 record current flag-off behavior, including route changes and clarification; they are not expectations for the new feature.

Verification without the snapshot-write environment variable:

- `dotnet test tests/FinancialCopilot.UnitTests --configuration Release --no-build --no-restore --filter FullyQualifiedName~CashFlowReport161RoutingBaselineTests` - 2 passed.
- `dotnet test tests/FinancialCopilot.IntegrationTests --configuration Release --no-build --no-restore --filter FullyQualifiedName~CashFlowReport161RoutingBaselineEndpointTests` - 3 passed.

Phase 3 T18 must re-run these baselines with the flag off and cover both V2 paths with the scripted model and additional endpoint and follow-up cases. No full solution test or live feature validation is claimed here.
