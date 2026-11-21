# Segment users by recorded question usage

## Feature Folder

`146-unified-user-management-and-customer-360/changes/recorded-user-usage`

## Layers / Area

backend, frontend · Administration

## Priority

P1

## Complexity

M

## Depends On

Feature 146 Users directory and the existing conversation and Billing stores.

## References

- [Feature 146](../../UserStory.md)
- [Design approval](design-proposal.md)

An administrator can find users by recorded question count and effective plan. The period controls default to the last 30 Tehran days and provide 7 days, 90 days, and a custom Jalali range. The zero-question label names the selected period. The list shows the last recorded question and, with ledger permission, credits charged before refunds.

Existing search and account filters combine with usage filters before pagination. Users with no retained turns in the selected period match the zero filter. Plan filtering and displayed plans agree at expiry and during overlapping windows. The response remains tenant-scoped and existing non-usage requests keep their original shape.
