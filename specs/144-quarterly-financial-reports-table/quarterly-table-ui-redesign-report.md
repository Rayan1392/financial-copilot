# Feature 144 — Quarterly Financial Table UI Refinement and PNG Export

## Cost-aware QoQ change colors

- Expense polarity is now based only on `SourceItemId` values 1, 5, 12, and 334. Since the frontend receives Markdown without row metadata, IDs are attached from Feature 144's fixed 16-row contract only when the complete ordered row set is present; partial tables remain unclassified.
- The shared pure color resolver is used by both the on-screen table and PNG canvas. Expense increases are red and decreases green; other rows keep positive green and negative red. Zero, opposite-sign, and unavailable comparisons use neutral colors.
- Displayed values and calculations are unchanged: the absolute-magnitude QoQ formula remains in place, output keeps one decimal and Persian digits, negative values are not parenthesized, opposite-sign rows stay blank, unavailable rows keep the dash, and share-count zero changes stay blank.
- The focused tests cover all approved expense IDs, expense/non-expense polarity, zero and unavailable states, opposite-sign and share-count special cases, original percentage values, and matching web/PNG semantic colors. The چخزر cases render Revenue +۲۷٫۵٪ green, cost of goods sold +۳۰٫۱٪ red, general/administrative expense +۱۱٫۱٪ red, and net profit +۴۵٫۹٪ green.

## Files changed

- `src/frontend/src/components/app/quarterly-financial-table.tsx`
- `src/frontend/src/components/app/quarterly-financial-table-colors.ts`
- `src/frontend/src/components/app/quarterly-financial-table-constants.ts`
- `src/frontend/src/components/app/quarterly-financial-table-image.ts`
- `src/frontend/src/components/app/__tests__/quarterly-financial-table-colors.test.ts`
- `src/frontend/src/components/app/__tests__/quarterly-financial-table.test.tsx`
- `src/frontend/src/components/app/__tests__/markdown-message.test.tsx`
- `src/frontend/src/components/app/__tests__/quarterly-financial-table-image.test.ts`
- `specs/144-quarterly-financial-reports-table/quarterly-table-ui-redesign-report.md`

The existing Markdown integration parses financial tables into the shared quarterly table component, so no separate formatting path was added.

## Table presentation and formatting

- Gross margin values are treated as percentage points already supplied by the report and rendered with one decimal place. The renderer adds only the Persian percent sign; it does not multiply the value again.
- Financial values, quarter and year labels, summary figures, row-label digits, percentages, and the export date use Persian digits. `Intl.NumberFormat` with the Persian numbering system supplies localized decimal and grouping separators.
- The header unit summary removes the redundant share-count unit while retaining monetary units and EPS units.
- Change values use the shared parsed model for both the table and PNG. Comparable nonzero values with the same sign use the absolute-value percentage formula; exact zero displays Persian zero without a percent sign. Share-count zero changes and sign mismatches are empty with a neutral white cell. Unavailable comparisons retain the existing dash.
- The header uses a restrained teal palette, coordinated with the beige label cells, yellow gross-margin highlight, and green/red change cells.
- PNG filenames include the symbol from the report title and a Persian-calendar date. Invalid filename characters and repeated separators are removed.

## PNG export consistency fixes

- The PNG exporter painted a header row, but rebuilt the header labels independently from the web table. That separate list could drift. The change-column title is now defined once in `quarterly-financial-table-constants.ts` and used by both renderers; the canvas test verifies it is drawn.
- The exported change-column header used a white fill while the web table uses teal (`#c7e5e1`). The PNG now uses the same fill. Header alignment follows the on-screen table: the description is right aligned and quarter/change headers are centered. RTL column order keeps `شرح` on the right and the change column on the left.
- Canvas change-value parsing did not normalize directional marks, Unicode minus signs, or the Persian percent sign. Negative percentages could therefore be treated as neutral. Parsing now handles those characters so the PNG keeps the same positive/negative background and text colors as the web table.
- The change-column title is now **درصد تغییر به فصل قبل** in the on-screen header and the exported PNG.
- Financial calculations, row order, and data semantics were not changed.

## Verification

- Ran the focused quarterly table and Markdown suite from `src/frontend`: **38 tests passed across 4 files**. It covers all approved expense IDs, positive/negative polarity for expense and other rows, zero/unavailable/opposite-sign/share-count states, unchanged Persian percentage text, the required چخزر values, and matching browser/PNG semantic colors.
- `npm run build` completed successfully. Vite reported its existing large-client-chunk and external-import warnings.
- Ran `npm test -- src/components/app/__tests__/markdown-message.test.tsx src/components/app/__tests__/quarterly-financial-table-image.test.ts`: **20 tests passed**. The DOM test checks the renamed title, RTL oldest-to-newest order, and teal header fill. The canvas test checks the complete change header text, teal fill, negative background/text colors, and safe ticker-bearing filename.
- Targeted ESLint completed with **0 errors**. It reports one existing Fast Refresh warning in `quarterly-financial-table.tsx`.
- `npm run build` completed successfully. Vite reported its existing large-client-chunk and external-import warnings.
- No browser screenshot was captured. Verification covers rendered DOM assertions and canvas draw calls/styles through the frontend test environment.

## Remaining limitations

- Actual browser font rasterization and pixel-level screenshot comparison were not exercised. Persian glyph appearance can vary by installed browser fonts.
- Symbol extraction uses the trailing parenthesized symbol in the report title; reports without one produce a safe filename without a symbol.
