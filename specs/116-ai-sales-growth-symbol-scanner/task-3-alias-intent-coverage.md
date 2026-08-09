# Task 3 — Alias and Intent Coverage

Status: Implemented

Feature 116 now uses the existing semantic catalog and scanner application
boundaries:

- `MetricAliasResolver` matches normalized aliases through
  `MetricAliasTextNormalizer`.
- Normalization handles Arabic/Persian kaf and yeh variants, ZWNJ/ZWJ/BOM,
  tatweel, Persian/Arabic digits, percent signs, decimal separators, spacing,
  and case.
- Monthly-sales YoY and MoM growth aliases include English and Persian forms
  and preserve the governed `GrowthComparison` qualifier.
- `SalesGrowthSymbolScannerIntentRules` covers the required list/discovery,
  sales, growth, and comparison vocabulary across Persian, English, and mixed
  messages.
- The intent rule recognizes percent and multiple notation (`%`, `درصد`,
  Persian digits, `x`, and `×`) without parsing or calculating a result.

Routing precedence is intentionally left to Task 4. The rule is a reusable
semantic predicate and is not yet wired into the active/rollback orchestrator.
Unknown or conflicting wording does not produce a plan from this task; it
falls through to the existing parser/clarification behavior.

Validation: `SalesGrowthAliasAndIntentTests` passes 12 tests.
