# BUG-002 — V2 Feature 125 payload is rendered as Unsupported

## Status

`FIXED IN CODE — PERSISTED DATA AVAILABILITY REMAINS A SEPARATE CONCERN`

- Severity: High
- Component: Feature 125 conversational response path
- Affected capability: `symbol_vs_industry_relative_valuation`
- Reproduction query: `نماد شگل را با صنعت خودش مقایسه کن`

## Summary

Feature 125 semantic routing and execution were registered correctly. The executor returned an
`IndustryRelativeValuationPayload` containing the persisted read model and presentation text, but
the Microsoft Agent Framework V2 workflow did not handle that payload type.

When the payload was not matched, the workflow used its generic fallback. Because the execution
status was `Executed` but the payload was unknown to the response switch, the fallback produced the
generic unsupported message instead of the Feature 125 presentation.

The fallback V2 runner also lacked the Feature 125 payload handling and capability-to-intent
mapping, so both V2 execution paths had the same contract gap.

## Root cause

`IndustryRelativeValuationCapabilityExecutor` returns:

```csharp
new IndustryRelativeValuationPayload(read, presentationText)
```

The active V2 workflow handled other typed semantic payloads but omitted
`IndustryRelativeValuationPayload`. Its default response branch therefore treated a successful
Feature 125 execution as unsupported. The V2 fallback runner had the same omission.

## Fix applied

- Added `IndustryRelativeValuationPayload` handling to the native V2 workflow.
- Added the same handling to the fallback V2 runner.
- Added Feature 125 capability mappings to `SemanticIntent` in both V2 paths.
- Added an architecture regression test covering both paths and all four Feature 125 capability
  codes.

## Scope boundary

This fix only addresses loss of a successful Feature 125 read response in V2. It does not create or
refresh persisted calculations. If the read repository cannot find a `Published` calculation with
`IsSelectedCurrent = true`, the executor will still correctly return `NoData`; that condition is
tracked separately in `bug-001-shgol-own-industry-comparison-returns-no-data.md`.

## Relevant files

- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/FinancialCopilotAgentWorkflowRunner.cs`
- `tests/FinancialCopilot.ArchitectureTests/CleanArchitectureDependencyTests.cs`
