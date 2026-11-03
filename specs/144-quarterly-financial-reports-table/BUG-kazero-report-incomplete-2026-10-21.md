# Bug: Kazero quarterly report has fallback values that the table did not show

## Production evidence

The user supplied a screenshot of Kazero's quarterly table and read-only SQL results for company `13212`, period end 1405/06/31. Production report `48b614d2-c398-49ca-971d-1afd53ce878c` is a separate six-month income statement, ending 2026-09-22, synchronized at 2026-10-21 07:16 UTC. It has 26 persisted line items. The corresponding stored raw provider payload also contains 26 source items. The count difference from other reports with 48 or 49 items is therefore present in the provider response; this report was not reduced from 48 rows by the persistence path.

The report has the supplemental values needed by the approved fallback policy, while the primary IDs are absent:

| Table row | Primary ID | Fallback ID | Current YTD value | Prior YTD value |
|---|---:|---:|---:|---:|
| Gross profit | 139 | 310 | 0 | 0 |
| Operating profit | 140 | 311 | -294,049 | -75,377 |
| Continuing profit before tax | 142 | 312 | -294,045 | -75,377 |
| Net profit | 143 | 313 | -294,045 | -75,377 |
| Optional raw EPS observations (not calculation inputs) | 160 | 317 | -239 | -61 |

Amounts are million rials except EPS, which is rials per share. Both reports show 1,230,000 thousand shares. Gross profit and revenue are valid zero values, so the table's zero-as-dash convention is correct for those rows. For the 1405 Q2 standalone column, adjacent-YTD subtraction should display operating profit `-218,672`, continuing profit before tax `-218,668`, and net profit `-218,668` (rendered with accounting parentheses). EPS derived from standalone net profit and the share basis is approximately `-178` rials per share.

## Root cause and correction

The varying source-item count is a provider-data characteristic for this report, not evidence of dropped persistence rows. The displayed profit rows depend on Feature 144's primary-first fallback mapping (`139 ← 310`, `140 ← 311`, `142 ← 312`, `143 ← 313`). The workspace implementation now retrieves those supplemental source IDs and selects them only if the primary row is absent or null, preserving the selected source identity for provenance.

EPS had an additional gap: its numerator lookup did not consistently use the selected net-profit row, and the old calculation also required a raw EPS observation (160 or 317) to reconcile. The calculation now uses item 143, or fallback 313 when 143 is absent/null, for both adjacent YTD numerator observations; it divides standalone net profit by the approved share basis. Raw items 160/317 are optional provenance and no longer gate or supply the displayed EPS calculation.

## Deployment note

These code and documentation changes are local to the workspace. The production screenshot predates deployment of the current fallback implementation, so the production table will continue showing the old result until this build is deployed. No production records or services were modified during this investigation.
