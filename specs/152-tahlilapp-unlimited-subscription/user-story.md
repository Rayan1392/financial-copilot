# Feature 152 — Max user stories and acceptance criteria

**Status:** pending implementation. [Specification](spec.md). Shared full-window and one-time-Free acceptance belongs to Feature 139 T-40–T-43; its completed criteria remain intact.

**US-152-01:** As a user with an active TahlilApp Max subscription, I can use permitted SapioAI operations without a numeric credit balance.

- **AC-01:** Active authoritative Max works with wallet balance zero. Authentication, enabled account, explicit capability limits and operational controls still apply. No arbitrary new quota is added.
- **AC-02:** Each completed request records operation, outcome, actual tokens/cost and original Unlimited source, with zero numeric credit debit or wallet reservation. Failure, cancellation, retry and usage refund cannot add fictitious credits.
- **AC-03:** A request admitted before natural expiry may finish under its stored Unlimited mode within its reservation lifetime; new requests at or after authoritative expiry cannot use Unlimited even before cleanup. Revocation releases pending reservations and rejects their later finalization.
- **AC-04:** Finite→Unlimited and Unlimited→finite/Free transitions, renewal and overlap preserve unrelated finite wallet/source balances. Existing finite reservations retain their original finite hold and charge behavior; committed replay returns the original result.
- **AC-05:** SapioAI uses TahlilApp's `expires_at` and never computes 90 days. Existing paid-expiry cooldown and revocation behavior include Max; paid expiry cannot create or reactivate a Free grant.
- **AC-06:** Usage and subscription APIs expose explicit quota mode and authoritative expiry while numeric balance/report fields stay numeric. Frontend renders «نامحدود» rather than numeric infinity and reverts display at expiry.
- **AC-07:** Existing Free/Plus/Pro authorization, grants, reservation, usage, expiry and refunds remain correct under the new full-window/one-time-Free policy. Max capabilities are defined explicitly and are not assumed equal to Pro; valid unrelated finite credits remain intact.

**Required tests, no test code in this task:** parser/map and active-window selection; capability matrix; zero-balance admission; commit/cancel/retry/refund with zero wallet mutation; plan change between reserve and finalize; expiry before cleanup; revocation release; renewal/overlap; API numeric compatibility and frontend display; PostgreSQL concurrency and finite-plan regression. Shared Plus/Pro full-window and Free-once tests are owned by Feature 139.
