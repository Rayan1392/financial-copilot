# Tasks - Frontend Monthly Activity Trend Chart

## Task 1 - Audit Current Assistant Message Rendering

Review the current chat assistant-content rendering path and identify where structured AI payloads
are mapped into frontend components.

Acceptance:

- The implementation point for `monthlyActivityTrendResult` is identified.
- Existing conversation reload rendering is included in the audit.

---

## Task 2 - Add Frontend Trend Chart View Model Mapping

Map `monthlyActivityTrendResult` into a frontend-friendly chart view model.

Required fields:

- Title
- Unit
- Fiscal month labels
- Previous-year bar series
- Current-year bar series
- 12-month average line series
- Missing-data notes

Rules:

- Preserve `null` values as missing/unreported periods.
- Do not convert missing values to zero.
- Do not derive extra financial values beyond display formatting.

---

## Task 3 - Implement Chart Component

Create a dedicated frontend component for the monthly activity trend chart.

Required behavior:

1. Render two bar series and one line series.
2. Use Persian month labels.
3. Show the company title and unit note.
4. Support mobile and desktop layouts.
5. Visually distinguish current year, previous year, and average line.

Recommended outcome:

- A chart visually close to the agreed product example, while still fitting the existing app design system.

---

## Task 4 - Integrate With Chat Message Rendering

Display the trend chart inside assistant messages when `monthlyActivityTrendResult` is present.

Required behavior:

1. The chart appears with the same assistant message as the trend text summary.
2. The chart also appears when old conversation messages are reloaded from persistence.
3. Non-trend assistant messages remain unchanged.

---

## Task 5 - Handle Partial And Missing Data States

Implement honest UI behavior for incomplete chart payloads.

Required behavior:

1. `null` current-year future months render as missing, not zero.
2. Missing previous-year points remain absent.
3. Missing-data notes from the payload are shown in a compact readable form when relevant.
4. The component gracefully handles sparse data without crashing.

---

## Task 6 - Tests And Verification

Add or update frontend tests covering:

1. Rendering when `monthlyActivityTrendResult` exists.
2. Conversation reload rendering for persisted assistant payloads.
3. Correct handling of `null` values.
4. No market quote fields shown in the trend chart block.

Verification:

- Relevant frontend tests pass.
- Frontend build passes.
- If there is a visual/storybook test harness, include the trend chart state there as well.
