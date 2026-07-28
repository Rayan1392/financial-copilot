# Bug: Insufficient Credit Error Silently Swallowed

## Summary

When a user sends a chat query but their account has insufficient spending capacity,
the backend throws an `InvalidOperationException` that is mapped to a generic HTTP 500
response. The frontend `sendMutation` has no `onError` handler, so the error is silently
dropped — the input unlocks but the user receives no feedback.

## Reproduction

1. Deplete or configure a test account to have zero available credits.
2. Send any chat message from the chat UI.
3. The input spinner stops. No error message appears. The query is silently lost.

## Root Cause

### Backend — wrong HTTP status, hidden message

`InvalidOperationException("Available spending capacity is insufficient.")` is thrown
in three places inside the Billing domain:

| File | Method |
|------|--------|
| `FinancialCopilot.Billing/Services/CreditReservationService.cs:34` | `ReserveAsync` |
| `FinancialCopilot.Billing/Services/WalletEntitlementService.cs:51` | `ValidateCanExecuteAsync` |
| `FinancialCopilot.Infrastructure/Billing/Persistence/UsageReservationAuthorizationService.cs:61` | `ReserveAsync` |

`GlobalExceptionMiddleware` has no case for this exception type; it falls to the default
arm and returns `HTTP 500` with:

```json
{
  "type": "https://financialcopilot/errors/internal-server-error",
  "title": "An unexpected error occurred.",
  "status": 500,
  "detail": "The request could not be completed."
}
```

The actual reason (`"Available spending capacity is insufficient."`) is hidden from the
client, making it impossible for the frontend to show a meaningful message.

### Frontend — no error handler on the chat mutation

`sendMutation` in both chat route files has only `onSuccess`; `onError` is absent.
React Query catches the thrown `Error` but nothing renders it.

## Fix

### Backend

1. Replace the three `InvalidOperationException` throws with a new domain exception
   `InsufficientCreditException` (in `FinancialCopilot.Billing`).
2. Add a case to `GlobalExceptionMiddleware` mapping `InsufficientCreditException`
   → `HTTP 402 Payment Required` with a stable error type URI and a safe, user-visible
   detail string.

### Frontend

1. In `_app.chat.tsx` (new conversation flow) and `_app.c.$threadId.tsx` (existing
   thread), add an `onError` handler to `sendMutation` / `newThreadMutation` that stores
   the error message in local state.
2. Render the stored error as a Persian inline banner below the prompt input:
   `"اعتبار کافی برای پردازش درخواست وجود ندارد. لطفاً حساب خود را شارژ کنید."`

## Acceptance Criteria

- [ ] Backend returns `HTTP 402` (not 500) when credits are exhausted.
- [ ] Response body has `type: "https://financialcopilot/errors/insufficient-credit"`.
- [ ] Frontend displays the Persian error message below the prompt input.
- [ ] Error clears when the user starts typing again.
- [ ] No change to the happy-path scanner flow or billing accounting.
- [ ] Unit test: `InsufficientCreditException` maps to 402 in `GlobalExceptionMiddleware`.
- [ ] Integration test: exhausted-credit query returns 402 with the correct error type.
