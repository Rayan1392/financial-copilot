# Fix long company name extraction in monthly sales questions

Status: Proposed — not approved

## Discovery

BUG-054 affects monthly activity trend requests. The trend intent recognizes «روند فروش ماهانه بازرگام را نشان بده», but `MonthlyActivityTrendIntentRules.ExtractCompanySymbol` excludes single-token symbol candidates longer than six characters. The seven-character «بازرگام» is dropped, so orchestration returns its missing-symbol clarification before querying the existing company resolver or monthly trend data.

## Proposed correction

Allow a single-token candidate up to seven characters in the monthly trend extractor. Preserve the twelve-character limit for joined and compound candidate forms, stop-word filtering, query routing, company resolution, and data retrieval behavior. Add regression coverage for the exact owner-reported question and verify that the extracted symbol is «بازرگام».

This restores company identification for a valid longer symbol. It introduces no new route, data, or query behavior.

## Acceptance

- «روند فروش ماهانه بازرگام را نشان بده» is still classified as a monthly activity trend and extracts «بازرگام».
- Existing ticker extraction and unknown-symbol clarification tests pass.
- When the resolver and stored trend data contain the company, orchestration receives its symbol and proceeds to the existing lookup.
