# Task 14 — Unit Tests

Completed the Feature 116 unit-test matrix by combining the existing focused tests with additional edge-case coverage.

## Covered

- Intent detection, routing precedence, alias recognition, Persian/Arabic normalization, ZWNJ handling, Persian/Latin digits, decimals, percent, and multiple forms.
- Strict versus inclusive operators and inferred default baseline/disclosure behavior.
- Previous-month, same-month-previous-year, and previous-12-month-average resolution.
- `2×` equals `100%` growth, invalid/missing/zero baselines, negative values, duplicate periods, and incomplete average windows.
- Deterministic sort/tie-break behavior and dynamic baseline column identifiers/titles.
- Common-period coverage and unavailable/partial selection states.
- Structured result-table policy, telemetry redaction, web rendering, and Telegram rendering.

## Verification

- `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --no-restore --filter FullyQualifiedName~SalesGrowth` — 71 passed.
