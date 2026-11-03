# Feature 152 — Max user stories and acceptance criteria

**Status:** in-repository implementation and focused verification complete for approved `sapio_max`, Pro-equivalent capabilities, and overlapping-window behavior. Joint staging/operations remain open. See [ImplementationReport.md](ImplementationReport.md). Shared full-window and one-time-Free acceptance belongs to Feature 139 T-40–T-43; its completed local criteria remain intact and its staging/operations gate is still open.

**US-152-01:** As a user with an active TahlilApp Max subscription, I can use permitted SapioAI operations without a numeric credit balance.

- **AC-01:** Active authoritative Max works with wallet balance zero. Authentication, enabled account, explicit capability limits and operational controls still apply. No arbitrary new quota is added.
- **AC-02:** Each completed request records operation, outcome, actual tokens/cost and original Unlimited source, with zero numeric credit debit or wallet reservation. Failure, cancellation, retry and usage refund cannot add fictitious credits.
- **AC-03:** A request admitted before natural expiry may finish under its stored Unlimited mode within its reservation lifetime; new requests at or after authoritative expiry cannot use Unlimited even before cleanup. Revocation releases pending reservations and rejects their later finalization.
- **AC-04:** Finite→Unlimited and Unlimited→finite/Free transitions, renewal and overlap preserve unrelated finite wallet/source balances. Existing finite reservations retain their original finite hold and charge behavior; committed replay returns the original result.
- **AC-05:** SapioAI uses TahlilApp's `expires_at` and never computes 90 days. Existing paid-expiry cooldown and revocation behavior include Max; paid expiry cannot create or reactivate a Free grant.
- **AC-06:** Usage and subscription APIs expose explicit quota mode and authoritative expiry while numeric balance/report fields stay numeric. Frontend renders «نامحدود» rather than numeric infinity and reverts display at expiry.
- **AC-07:** Existing Free/Plus/Pro authorization, grants, reservation, usage, expiry and refunds remain correct under the new full-window/one-time-Free policy. Max has explicit capability rows exactly equal to the approved Pro matrix, including limits; valid unrelated finite credits remain intact.
- **AC-08:** TahlilApp slug `sapio_max` maps to Max in exchange and status parsing, and the authoritative source expiry is preserved. The existing Pro-over-Plus choice is retained for exact non-Max expiry ties.
- **AC-09:** Active Max controls quota mode until its own expiry, without consuming overlapping finite grants. After Max expires, the latest-expiring active non-Max window controls plan/capabilities. Finite plan and top-up grants are summed for spending and retain independent source expiries through reservation, settlement, and refund.

**Required tests, no test code in this task:** parser/map and active-window selection; capability matrix; zero-balance admission; commit/cancel/retry/refund with zero wallet mutation; plan change between reserve and finalize; expiry before cleanup; revocation release; renewal/overlap; API numeric compatibility and frontend display; PostgreSQL concurrency and finite-plan regression. Shared Plus/Pro full-window and Free-once tests are owned by Feature 139.
