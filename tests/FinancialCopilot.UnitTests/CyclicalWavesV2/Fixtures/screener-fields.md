# CyclicalWaves v2 screener fields (29)

Source: tahlilapp-api-client.pdf §3.2 and the live `GET /custom-filtering/ticker/{ticker}` sample (2026-10-31).
Generated from `ScreenerMetricCatalog` (the single owner); ids are stable database keys.

| Id | Field key | English name | Persian name | Category | Unit |
|---|---|---|---|---|---|
| 1 | `lastQuarterSale` | Last quarter sales | فروش آخرین فصل مالی | QuarterlySales | Irr |
| 2 | `penultimateQuarterSale` | Penultimate quarter sales | فروش فصل ماقبل آخر | QuarterlySales | Irr |
| 3 | `lastYearSameQuarterSale` | Same quarter last year sales | فروش فصل مشابه سال قبل | QuarterlySales | Irr |
| 4 | `average4QuarterSale` | Average sales of the four quarters before the last quarter | میانگین فروش چهار فصل پیش از فصل آخر | QuarterlySales | Irr |
| 5 | `lastQuarterNetProfit` | Last quarter net profit | سود خالص آخرین فصل | Profit | Irr |
| 6 | `penultimateQuarterNetProfit` | Penultimate quarter net profit | سود خالص فصل ماقبل آخر | Profit | Irr |
| 7 | `lastYearSameQuarterNetProfit` | Same quarter last year net profit | سود خالص فصل مشابه سال قبل | Profit | Irr |
| 8 | `lastQuarterGrossProfit` | Last quarter gross profit | سود ناخالص آخرین فصل | Profit | Irr |
| 9 | `penultimateQuarterGrossProfit` | Penultimate quarter gross profit | سود ناخالص فصل ماقبل آخر | Profit | Irr |
| 10 | `lastYearSameQuarterGrossProfit` | Same quarter last year gross profit | سود ناخالص فصل مشابه سال قبل | Profit | Irr |
| 11 | `lastQuarterOperatingProfit` | Last quarter operating profit | سود عملیاتی آخرین فصل | Profit | Irr |
| 12 | `penultimateQuarterOperatingProfit` | Penultimate quarter operating profit | سود عملیاتی فصل ماقبل آخر | Profit | Irr |
| 13 | `lastYearSameQuarterOperatingProfit` | Same quarter last year operating profit | سود عملیاتی فصل مشابه سال قبل | Profit | Irr |
| 14 | `lastQuarterNetProfitMargin` | Last quarter net profit margin | حاشیه سود خالص آخرین فصل | ProfitMargin | Percent |
| 15 | `penultimateQuarterNetProfitMargin` | Penultimate quarter net profit margin | حاشیه سود خالص فصل ماقبل آخر | ProfitMargin | Percent |
| 16 | `lastYearSameQuarterNetProfitMargin` | Same quarter last year net profit margin | حاشیه سود خالص فصل مشابه سال قبل | ProfitMargin | Percent |
| 17 | `lastQuarterGrossProfitMargin` | Last quarter gross profit margin | حاشیه سود ناخالص آخرین فصل | ProfitMargin | Percent |
| 18 | `penultimateQuarterGrossProfitMargin` | Penultimate quarter gross profit margin | حاشیه سود ناخالص فصل ماقبل آخر | ProfitMargin | Percent |
| 19 | `lastYearSameQuarterGrossProfitMargin` | Same quarter last year gross profit margin | حاشیه سود ناخالص فصل مشابه سال قبل | ProfitMargin | Percent |
| 20 | `lastQuarterOperatingProfitMargin` | Last quarter operating profit margin | حاشیه سود عملیاتی آخرین فصل | ProfitMargin | Percent |
| 21 | `penultimateQuarterOperatingProfitMargin` | Penultimate quarter operating profit margin | حاشیه سود عملیاتی فصل ماقبل آخر | ProfitMargin | Percent |
| 22 | `lastYearSameQuarterOperatingProfitMargin` | Same quarter last year operating profit margin | حاشیه سود عملیاتی فصل مشابه سال قبل | ProfitMargin | Percent |
| 23 | `average12MonthSale` | Average monthly sales over 12 months, excluding the latest month | میانگین فروش ۱۲ ماه، بدون احتساب تازه‌ترین ماه | MonthlySales | Irr |
| 24 | `lastYearAverage12MonthSale` | Average monthly sales of the previous fiscal year | میانگین فروش ماهانه سال مالی قبل | MonthlySales | Irr |
| 25 | `lastMonthSale` | Last month sales | فروش آخرین ماه | MonthlySales | Irr |
| 26 | `penultimateMonthSale` | Penultimate month sales | فروش ماه ماقبل آخر | MonthlySales | Irr |
| 27 | `lastYearSameMonthSale` | Same month last year sales | فروش ماه مشابه سال قبل | MonthlySales | Irr |
| 28 | `pe` | Price to earnings (P/E) | نسبت قیمت به سود (P/E) | Valuation | Ratio |
| 29 | `ps` | Price to sales (P/S) | نسبت قیمت به فروش (P/S) | Valuation | Ratio |

`pe = 0` means "not computable" (for example negative earnings); `null` means unknown. Money is IRR; margins are percentages.
