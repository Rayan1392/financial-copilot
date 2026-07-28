# Telegram AVG 12M Monthly Sales Metric Code Label

Date: 2026-07-21
Status: Fixed in current working tree

## Observed Behavior

Telegram rendered a single-symbol metric card for `AVG_12M_MONTHLY_SALES` with the raw canonical metric code:

```text
AVG_12M_MONTHLY_SALES: ۱٬۴۲۱٬۳۶۳
```

The web response for the same query used the expected Persian display label:

```text
متوسط فروش ۱۲ ماهه
```

## Expected Behavior

Telegram symbol lookup cards must use the same Persian user-facing metric label as the web response. Raw canonical metric codes are internal identifiers and must not appear in end-user Telegram output when a Persian label exists.

For average 12-month monthly sales, Telegram must render:

```text
متوسط فروش ۱۲ ماهه: ۱٬۴۲۱٬۳۶۳ میلیون ریال
```

## Root Cause

`TelegramAssistantResponseRenderer.GetMetricLabel` had a local metric-code-to-label map for compact Telegram cards. That map included monthly sales, YTD sales, and prior-period monthly sales, but it did not include `AVG_12M_MONTHLY_SALES`.

Because the metric was missing from the Telegram-specific map, the fallback returned the raw metric code. The Telegram monetary-unit check also omitted `AVG_12M_MONTHLY_SALES`, so the compact card could miss the million-rial unit even though the web table displayed the metric correctly.

## Fix

- Added `AVG_12M_MONTHLY_SALES -> متوسط فروش ۱۲ ماهه` to the Telegram metric label map.
- Added `AVG_12M_MONTHLY_SALES` to the Telegram monthly monetary metric set so it renders with `میلیون ریال`.

## Regression Coverage

- `Single_symbol_average_12_month_sales_uses_localized_telegram_label`

## Affected Files

- `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs`
- `tests/FinancialCopilot.UnitTests/TelegramAssistantResponseRenderer089Tests.cs`
