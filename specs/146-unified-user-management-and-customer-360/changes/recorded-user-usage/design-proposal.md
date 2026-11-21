# Recorded user usage in the Users directory

Status: Gate A: Approved by the feature owner on 2026-11-21

Use the existing tenant-scoped Users directory for retained, recorded questions and effective plan filtering. Count stored user messages and show the latest retained question. Show gross posted Billing charges to authorized readers. The usage filters default to the last 30 Tehran calendar days and support 7 days, 90 days, and a custom Jalali range.

Stored turns can include retries and Telegram callbacks that are indistinguishable from questions. Deleted conversations and requests that failed before message persistence can reduce counts. The existing message and Billing stores supply the report. Schema and infrastructure changes are outside scope.
