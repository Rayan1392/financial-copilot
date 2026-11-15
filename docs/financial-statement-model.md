# Financial Statement Data Model

Reference for the `FinancialStatements` table introduced by spec `005` and refined by spec
`029`. The table holds normalized financial-statement rows that scanner queries and the derived
metric engine both consume.

## The two-column distinction: `StatementType` vs. `PeriodType`

A common confusion when this model was first laid out: a "type" word can mean either the kind
of statement (income / balance / cashflow) or the duration of the period it covers (one quarter
vs. half a year vs. full year). Spec `029` made these two columns explicit.

| Column | Domain values | Meaning |
|---|---|---|
| `StatementType` | `IncomeStatement`, `BalanceSheet`, `CashFlow` (enum: `FinancialCopilot.Domain.Financial.Entities.FinancialStatementType`) | The kind of statement. Drives which line items can validly appear (`NET_PROFIT` only on income, `TOTAL_EQUITY` only on balance). |
| `PeriodType` | `ThreeMonths`, `SixMonths`, `NineMonths`, `TwelveMonths`, `Monthly`, `TrailingTwelveMonths` (enum: `FinancialCopilot.Domain.Financial.Periods.FiscalPeriodType`) | The duration of the period the statement covers. Used by `LineItemMetricInputSource` to parse the row back into a `FiscalPeriod` for the derived metric engine. |

Both columns store the **string form** of their enum (e.g. `"ThreeMonths"`, not `"3"`) so they
round-trip cleanly via `Enum.Parse<T>`.

## Natural key

The unique index is `(ProviderName, ExternalStatementId, StatementType)`. The CodalDb provider
writes two rows per source statement — one `IncomeStatement` and one `BalanceSheet` — sharing
the same `ExternalStatementId` (the CodalDb `StmtId`). Earlier CodalDb code disambiguated by
suffixing `":INC"` / `":BS"` onto `ExternalStatementId`; spec `029` removed that workaround in
favor of the new column.

## Configured-provider JSON contract

The configured HTTP provider's `StatementDocument` payload (see
`FinancialStatementPayloadNormalizer`) requires both fields:

```json
{
  "statementId": "abc-123",
  "companyId": "C001",
  "netProfit": 500000,
  "period": "ThreeMonths",
  "statementType": "IncomeStatement",
  "periodStart": "2026-01-01",
  "periodEnd": "2026-03-31"
}
```

The normalizer validates both `period` and `statementType` against their respective enums and
throws `FinancialProviderException(InvalidResponse, …)` if either is unknown. This is the
guardrail against the spec-020 class of bug where a string field got the wrong taxonomy written
into it.

## Known approximation: balance-sheet period semantics

Balance-sheet values are point-in-time snapshots (e.g. equity *as of* March 31), not period
flows. Today the schema stores them with the same `PeriodType` as their parent statement
(`"ThreeMonths"` etc.), which is semantically imprecise but doesn't break any current
calculation. A future story may introduce a `Snapshot` period type or a `PointInTimeDate`
column; until then, downstream consumers should treat balance-sheet rows as snapshots taken at
`PeriodEnd`.

## Cash flow

Feature 161 reads normalized cash-flow observations without changing the stored source values.
For Codal presentation, displayed operating cash flow is derived per period as item 122 minus
item 121. Item 128 is an independent cross-check where reported; it must not replace that
calculation. The report preserves each source item and records the source IDs used by derived
values.

Cash-flow statements are selected by company, statement scope, registered template, reporting
period, and currency scale. Cumulative 3/6/9/12-month observations are differenced to produce
independent quarters only when the fiscal-period sequence is supported. Balance-sheet rows
remain point-in-time values and are never differenced.

The Codal reconciliation uses `(122 - 121) + 121 + 123 = 124`. A separately reported foreign-
exchange effect is shown as its own reconciliation step and is not included in operating,
investing, or financing cash flow. Missing, zero, and not-reported cells remain distinct in the
typed report payload.
