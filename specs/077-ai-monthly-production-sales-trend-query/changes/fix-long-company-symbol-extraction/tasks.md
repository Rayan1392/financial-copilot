# Fix long company name extraction implementation plan

Status: Draft — Gate A and Gate B not approved

- [ ] Raise the monthly trend extractor's single-token candidate limit from six to seven characters in `MonthlyActivityTrendIntentRules.cs`.
- [ ] Add a regression case for «روند فروش ماهانه بازرگام را نشان بده» to `MonthlyActivityTrend077Tests.cs`, asserting that the trend intent is preserved and «بازرگام» is extracted. Retain the existing unknown-symbol clarification assertion.
- [ ] Run the focused monthly-trend unit tests and inspect the diff.

No database, migration, API contract, provider, or ingestion changes are in scope. The fix enables the existing company resolver to receive the full name and look up the existing company and its monthly trend records.
