# Follow-up actions for the first three industry comparison symbols

Status: Approved by the owner on 2026-11-24 (Gate A)

After a successful own-industry comparison, present one existing chat follow-up action for each of the first three symbol rows, in the same order. Its Persian label is «بررسی سهم ‹نماد›» and it submits «سهم ‹نماد› را بررسی کن». Use the equivalent wording in English replies. A benchmark row does not count. Fewer than three visible symbols yields fewer actions. These actions offer further examination of the symbols.

Use the structured industry comparison result and the existing V2 follow-up pipeline. The action order does not depend on viewport width. Use the current web and Telegram presentation for suggested actions.

## Revision 2026-11-24 — compare each symbol with its own industry

Status: Gate A: Approved by the owner on 2026-11-24

After v1.25.0 the owner corrected the action: each of the first three symbol rows gets one action whose label and submitted message are both «‹نماد› را با صنعت خودش مقایسه کن» (English: "Compare ‹symbol› with its industry"). The action runs the own-industry comparison (`symbol_vs_industry_relative_valuation`) for that symbol instead of the stock review. The rest of the design (first three member rows in display order, benchmark excluded, fewer rows give fewer actions, existing web and Telegram presentation) is unchanged. The actions are offered only while the own-industry comparison is available from the active data source.
