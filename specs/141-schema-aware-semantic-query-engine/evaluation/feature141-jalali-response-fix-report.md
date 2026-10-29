# Feature 141 — Persian (Jalali) Date Rendering Fix (2026-10-29)

Verdict: **FEATURE141_JALALI_RENDERING_PASSED**

Every reporting date in Feature 141's user-visible answers is now written in the Jalali calendar, with Persian digits, and
no Gregorian date appears. Conversion uses `System.Globalization.PersianCalendar` and is deterministic; the LLM does no
calendar conversion. The original Gregorian `DateOnly` values are unchanged everywhere else: database queries, SQL
parameters, executor rows, answer evidence, table rows and traces. No changes were made to SQL, schema, calculations,
routing or billing.

## Changed files

| File | Change |
|---|---|
| `Application/.../Navigation/PersianReportingPeriodText.cs` (new, internal) | Builds all Jalali date text from `PersianCalendar`; a single date renders as `۳۱ خرداد ۱۴۰۵`. A window covering exactly one Jalali month renders as `شهریور ۱۴۰۵`; any other window renders as `بازهٔ … تا …`. A statement period is named from its reporting metadata (period type plus bounds): `دوره سه‌ماهه / شش‌ماهه / نه‌ماهه منتهی به …` or `سال مالی منتهی به …`. If the period type is unknown, or the start is not the matching number of whole Jalali months before the end, both bounds are stated instead (`… از … تا …`), so no period information is lost. A missing end renders as `دوره گزارش نامشخص`. |
| `Application/.../Navigation/PersianStructuredQueryResponseGenerator.cs` | Single-company, monthly, time-series, company-comparison, producer-ranking, discovery, no-match and no-data text now uses the formatter. The evidence facts also carry `PeriodType`, so a statement period can be named. |
| `Application/.../Navigation/PersianStatementComparisonRenderer.cs` | Comparison, ranking and filter headers state the shared period in Jalali; the former `… شمسی؛ … میلادی` dual form is gone. The newer-period and no-common-period disclosures use Jalali dates. Comparison rules are unchanged. |
| `tests/FinancialCopilot.UnitTests/Feature141JalaliRenderingTests.cs` (new) | 18 focused tests (below). |
| `tests/.../PersianStructuredQueryResponseGeneratorTests.cs` | The existing assertion "the period appears once" now checks the Jalali form. |
| `tests/.../Feature141EngineVerticalSliceTests.cs`, `Feature141ChatMvpTests.cs` | Live assertions require the Jalali period. The chat harness also checks that no Feature 141 answer contains a Gregorian year or `میلادی`. |

## Before and after (live chat answers, same fixture)

| Question | Before | After |
|---|---|---|
| C1 سود خالص کگل | `کگل در بازهٔ ۲۰۲۶-۰۳-۲۱ تا ۲۰۲۶-۰۶-۲۱، مقدار مالی گزارش‌شده: ۱٬۲۰۰ میلیون ریال.` | `کگل در دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵، مقدار مالی گزارش‌شده: ۱٬۲۰۰ میلیون ریال.` |
| C2 تولید گندله کگل (شهریور ۱۴۰۵) | `کگل گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار تولید ماهانه: ۲۵ تن.` | `کگل گندله در شهریور ۱۴۰۵، مقدار تولید ماهانه: ۲۵ تن.` |
| C4 مقایسه | `… (دورهٔ سه‌ماهه ۱۴۰۵/۰۱/۰۱ تا ۱۴۰۵/۰۳/۳۱ شمسی؛ ۲۰۲۶-۰۳-۲۱ تا ۲۰۲۶-۰۶-۲۱ میلادی):` | `مقایسهٔ سود خالص در آخرین دورهٔ گزارش مشترک (دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵):` |
| C5 رتبه‌بندی | same dual form | `… بر اساس سود خالص از بیشترین به کمترین، در آخرین دورهٔ گزارش مشترک (دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵):` |
| C6/C7 فیلتر | same dual form | `… با شرط «سود خالص بیشتر از ۱٬۰۰۰ میلیون ریال»، در آخرین دورهٔ گزارش مشترک (دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵):` |
| C8 کشف تولیدکنندگان | `در گزارش‌های ماهانهٔ واجد شرایط بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲ … ۲۵ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲` | `در گزارش‌های ماهانهٔ واجد شرایط شهریور ۱۴۰۵ … ۲۵ تن در شهریور ۱۴۰۵` |
| Newer-period disclosure | `… دورهٔ جدیدتری تا ۱۴۰۵/۰۳/۳۱ گزارش شده …` | `… دورهٔ جدیدتری تا ۳۱ خرداد ۱۴۰۵ گزارش شده …` |

Values, units, company labels, rankings, exclusions and statement basis are identical before and after. The values come
from the same executor rows, and the 30 engine vertical slices still assert the same SQL, parameters, values and independent
database reads.

## Test results

| Run | Scope | Result | Exit code |
|---|---|---|---|
| Focused unit tests | `Feature141JalaliRenderingTests` (18): three-, six- and nine-month periods and annual periods, including a non-Farvardin fiscal year and leap Esfand 30, 1403; leap-year boundary dates; monthly windows (Shahrivar, leap and common Esfand, partial window); monthly production and sales answers; comparison, ranking and filter answers; the newer-period disclosure; the discovery answer; missing or inconsistent metadata | 18/18 passed | 0 |
| Renderer-related unit tests | generator, recorded-response replay, cross-company and Jalali tests | 71/71 passed | 0 |
| Full unit suite | `FinancialCopilot.UnitTests` | 2,475 total, 2,455 passed, 20 failed: the same 20 pre-existing failures as before | 1 |
| **Live regression** | `Feature141ChatMvpTests` (9 chat questions + flag-off rollback) and `Feature141EngineVerticalSliceTests` (30) | 39/40. The one failure, E04, was an old assertion that required the Gregorian end date in the answer. The answer itself was correct (`کگهر در دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵ …`); the assertion was updated to the Jalali form and E04 re-run live | 1, then E04 re-run **passed** (0) |
| Offline Feature 141 integration | `Feature141*` and `MonthlyStructuredQuery*` without live opt-in | 38 passed, 41 skipped (live), 1 failed | 1 |

The offline failure, `CompiledMonthlyRetrievalAndTimeSeriesExecuteThroughRestrictedReadOnlyExecutor`, asserts the catalog
authorization state (line 780, `QueryAuthorized`). It fails identically on pristine HEAD `ff73d3c` (`D:\Source\fc-base141`),
so it is unrelated to rendering.

Artifacts: `evaluation/artifacts/feature141-chat-mvp/2026-10-29-1039*..1042*-C*.json` and `jalali-live-run.log`.

## Remaining issues

- In the live run, C3 was answered by the existing specialist `product_sales_value`, which renders its own period as
  `دوره ۱۴۰۵/۰۶`. That is Jalali, in the specialist's own format, and outside Feature 141's renderers.
- The engine-test fixture's prior quarter starts on 30 Azar 1404, which is not a Jalali month boundary. It is therefore
  rendered with both bounds (`دوره سه‌ماهه از ۳۰ آذر ۱۴۰۴ تا ۲۹ اسفند ۱۴۰۴`) instead of the short form, as designed.
  Real Iranian statement periods align with Jalali months and use the short form.
- Raw warning codes that the generic renderer appends (`هشدار: …`) are passed through unchanged. Feature 141 currently emits
  no date-bearing warning on that path; the comparison warnings with dates are translated by the comparison renderer.
