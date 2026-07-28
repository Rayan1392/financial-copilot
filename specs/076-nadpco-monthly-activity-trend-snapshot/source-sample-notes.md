# Source Sample Notes - Company 194 / کهمدا

The attached NADPCO samples for companyId 194 show the expected semantics:

- outputTypeId 0: single-month period; use this for monthly chart bars.
- outputTypeId 1: fiscal-year-to-date; use this for YTD context.
- outputTypeId 2: adjustments; not used in v1 trend chart.
- outputTypeId 3: adjusted YTD-to-previous-month; not used in v1 trend chart.
- outputTypeId 4: fiscal-year-to-previous-month; use this for YTD previous-month context.

For 1405/03 in the sample, outputTypeId 0 contains three non-zero monetary product rows:

- return line: -17,866
- export sales: 1,157,195
- domestic/product sales: 2,601,677

Net monthly sales = 3,741,006 million Rials.

This confirms the spec rule that negative values such as returns must be included in net monthly sales and that outputTypeId 1/4 must not be used to construct the monthly bar when outputTypeId 0 exists.
