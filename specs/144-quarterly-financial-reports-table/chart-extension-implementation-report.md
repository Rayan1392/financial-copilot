# Feature 144 chart and PNG branding — implementation evidence (2026-10-21)

The Feature 144 web table includes an optional comparison chart between its summary and financial rows. Sales, gross profit, and net profit use grouped monetary bars; Gross Margin uses a separate percentage line. The chart reads the same parsed quarter values as the table and preserves the backend's available-zero (`-`) versus unavailable (`—`) distinction. The parser supports 1–12 periods.

## Chart orientation update (2026-10-23)

The table model supplies periods newest first. Both web and PNG chart renderers now explicitly reverse that order and plot chronologically from left to right: oldest on the left, newest on the right. The table and page keep their RTL layout.

The API exposes the effective non-secret `QuarterlyFinancialTable:ShowChart` setting at `GET /api/ui/v1/config`. `appsettings.json` defaults it to `true`; `.env` can override it with `QuarterlyFinancialTable__ShowChart=false`. The frontend waits for this value before enabling image download, so the web and PNG use the same setting. Configuration is read again when a table mounts, including conversation replay.

The PNG adds a Sapio masthead with an Asia/Tehran Persian export date, then the optional chart, the existing 16-row table, and a Sapio attribution footer. With the setting disabled, the chart section is omitted while the branding remains.

## Verification

- API project build passed with no warnings or errors.
- Frontend production build passed.
- Focused Feature 144 frontend tests passed: 13/13 across table, chart, and PNG suites. They cover 1/12-quarter parsing, series values/order, negative/zero/missing states, configuration enabled/disabled, and image text/layout.
- Focused .NET configuration precedence test passed: 1/1, checking the JSON default and environment override.
- Headless browser PNG check passed for chart enabled and disabled. It verified 16 table rows, quarter/header alignment, gross-margin highlighting, no document-width overflow at a 390-pixel viewport, chart/brand/footer text, and the expected PNG height difference.
- Visual evidence: `png-header-evidence/quarterly-sharak-chart-enabled.png`, `quarterly-sharak-chart-disabled.png`, and `quarterly-sharak-web-chart-enabled.png`.

The repository-wide TypeScript type check still reports pre-existing errors in unrelated routes, admin/test fixtures, and monthly chart components. The Feature 144 files did not add a reported type error. No database migration or financial calculation path changed.
