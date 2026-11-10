# Feature 158: per-symbol causes

Source: `read-only-checks.sql`, run by the owner on production on 2026-11-10 in a read-only session. Only public tickers, company names, and counts.

| Symbol | Company | Label | Plain reason | Reply after this feature | Follow-up |
|---|---|---|---|---|---|
| ثجنوب | سرمایه گذاری مسکن جنوب | C | Reporting type 1000001 has no monthly route | Not-covered reply | Vendor question (OQ-026) |
| ولساپا | لیزینگ رایان سایپا | C | Reporting type 1000004 has no monthly route | Not-covered reply | Vendor question (OQ-026) |
| تابان | پتروشیمی تابان فردا | C | Reporting type 1000002 has no monthly route | Not-covered reply | Vendor question (OQ-026) |
| دسبحا | گروه دارویی سبحان | C | Reporting type 1000002 has no monthly route | Not-covered reply | Vendor question (OQ-026) |
| والبر | گروه سرمایه گذاری البرز | C | Reporting type 1000002 has no monthly route | Not-covered reply | Vendor question (OQ-026) |
| ومعادن | سرمایه گذاری توسعه معادن و فلزات | C | Reporting type 1000002 has no monthly route | Not-covered reply | Vendor question (OQ-026) |
| ثهام | صندوق س ثروت هامرز-سهام | B | A fund, not eligible; reporting type is null | Outside-scope sentence | None |
| سابیک | Not in the catalog | A | No row under any provider | Unchanged | Catalog review |
| غشکو | Not in the catalog | A | No row under any provider | Unchanged | Catalog review |
| فسبزوار | پارس فولاد سبزوار | G | Data present: 19 snapshots to 1405/07, 95 accepted reports | Data shown | Investigate the turns |
| فملی | ملی صنایع مس ایران | G | Data present: 19 snapshots to 1405/07, 95 accepted reports | Data shown | See below |
| فولاد | فولاد مبارکه اصفهان | G | Data present: 19 snapshots to 1405/07, 95 accepted reports | Data shown | See below |

## Findings

- **The duplicate-row explanation for فملی and فولاد is refuted on current data.** Each has one Noavaran row, one eligible row, and no row under another provider, so the exact steps resolve them today (`resolved_alias`). The recorded `entity_ambiguous` replies have another cause, not yet known. Canonical priority and D5 do not address it.
- **Population.** No eligible company has another provider's row with its symbol in `Ticker` (`ambiguous_on_ticker` 0, `one_other_ticker_row` 0). `TseSymbol` and `CompanySymbol` collisions were counted only for the listed symbols, where there are none.
- **Reach of the not-covered reply.** 287 of 877 eligible companies have a no-route type: 1000002 179, 1000006 38, 1000001 28, 1000003 17, 1000004 15, 1000009 10. Type 1000007 has no eligible company. None of the 287 has a monthly snapshot.
- **Routed types.** 1000000: 436 of 480 have a snapshot. 1000005: 72 of 88. 1000008: 19 of 22.
- **Provider sync.** The six no-route symbols above have 31 to 35 failed `MonthlyProductionSales` runs each in 365 days, except تابان with 6. `last_error` in the query is a lexical maximum, not the latest error, so it does not say why each run failed.

## Recorded replies, last 180 days

Aggregated counts from `Messages` for questions naming a symbol with the word for sales, excluding the automation actor. Run by the owner on 2026-11-10. No question text.

- `entity_ambiguous` appears on 4 turns only: `فملی` 2 and `فولاد` 2. All four are on product capabilities (`product_sales_trend` 3, `product_revenue_mix` 1). None has a separator or names both symbols.
- `monthly_activity_trend` for `فملی` is always answered (outcome 0). For `فولاد` it is answered on 16 turns and `supported_but_no_rows` on 2. It is never `entity_ambiguous`.
- So the recorded ambiguity is not a monthly-sales defect and is not caused by duplicate company rows. Its cause was not reproduced; question text was not read.
- `فسبزوار`: answered in August; `supported_but_no_rows` on all 6 turns in November (3 on `monthly_activity_trend`, 3 on `product_sales_trend`), although snapshots exist as of 2026-11-10. Not explained: the data may have arrived after those turns.
