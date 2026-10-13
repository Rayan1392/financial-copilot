# Feature 144 — Quarterly Financial Table UI Refinement

## Files changed

- `src/frontend/src/components/app/quarterly-financial-table.tsx`
- `src/frontend/src/components/app/quarterly-financial-table-image.ts`
- `src/frontend/src/components/app/__tests__/markdown-message.test.tsx`
- `src/frontend/src/components/app/__tests__/quarterly-financial-table-image.test.ts`
- `specs/144-quarterly-financial-reports-table/quarterly-table-ui-redesign-report.md`

The existing Markdown integration already parses the financial table into the shared quarterly table component, so no separate formatting path was added.

## Presentation and formatting changes

- Gross margin values are treated as percentage points already supplied by the report and rendered with one decimal place. The renderer adds only the Persian percent sign; it does not multiply the value again.
- Financial values, quarter and year labels, summary figures, row-label digits, percentages, and the export date use Persian digits. `Intl.NumberFormat` with the Persian numbering system supplies localized decimal and grouping separators.
- The header unit summary removes the redundant share-count unit while retaining monetary units and EPS units.
- Change values use the shared parsed model for both the table and PNG. Comparable nonzero values with the same sign use the absolute-value percentage formula; exact zero displays Persian zero without a percent sign. Share-count zero changes and sign mismatches are empty with a neutral white cell. Unavailable comparisons retain the existing dash.
- The header now uses a restrained teal palette, coordinated with the beige label cells, yellow gross-margin highlight, and green/red change cells.
- PNG filenames include the symbol from the report title and a Persian-calendar date. Invalid filename characters and repeated separators are removed.

## Verification performed

- Ran `npm test -- src/components/app/__tests__/markdown-message.test.tsx src/components/app/__tests__/quarterly-financial-table-image.test.ts` from `src/frontend`: **20 tests passed**.
- The table test checks Persian-rendered numbers, one-decimal gross margin, zero and sign-mismatch behavior, share-count blank behavior, RTL layout, and the simplified summary.
- The image-export test stubs canvas drawing, checks that the gross-margin label is drawn as a Persian-digit percentage, and verifies a ticker-bearing safe PNG filename.
- Targeted ESLint completed with no errors; it reports two existing warnings (an unused test suppression and the component file export pattern).
- `npm run build` completed successfully.
- No browser screenshot was captured. Verification covers rendered DOM assertions and the canvas export call through the frontend test environment.

## Remaining limitations

- Actual browser font rasterization and pixel-level screenshot comparison were not exercised. Persian glyph appearance can vary by the fonts installed in the browser environment.
- Symbol extraction uses the trailing parenthesized symbol in the report title; reports without one produce a safe filename without a symbol.
