-- Change valuation-gauge-forward-ratio, task 6.1: read-only coverage of Sapio's forward estimate on the gauge tickers.
-- Mirrors ForwardRatioInputsReader (unique ISIN match, accepted month-itself reports, latest 16 months, latest 8
-- statements, revenue keys 44 > 46 > 124 > 412 > 599, net profit 66) and the ForwardRatioEstimator guards in their order
-- (method sapio-1, shipped defaults: industry 31 excluded, 16 consecutive months with gaps <= 35 days, latest month at
-- most 75 days old, growth 0.5-4, 8 consecutive usable quarters with gaps <= 100 days). Gauge status and card
-- displayability are not checked here, so the counts are an upper bound for what the cards show.
-- Run read-only, for example:
--   PGOPTIONS="-c default_transaction_read_only=on -c statement_timeout=120000" psql ... -f 14_forward_estimate_coverage.sql
with gauge as (
    select t."Id" ticker_id, t."Ticker" ticker, upper(trim(t."Enticker")) isin,
           max((case when g."CloseSign" < 0 then -1 else 1 end) * g."CloseLow" / power(10::numeric, g."CloseScale"))
               filter (where g."GaugeType" = 'PE') pe_close,
           max((case when g."CloseSign" < 0 then -1 else 1 end) * g."CloseLow" / power(10::numeric, g."CloseScale"))
               filter (where g."GaugeType" = 'PS') ps_close
    from cyclicalwaves."Tickers" t
    join cyclicalwaves."GaugeDetails" g on g."TickerId" = t."Id"
    where t."IsActive"
    group by 1, 2, 3
),
company as (
    select g.ticker_id, count(*) matches, min(c."ProviderName") provider, min(c."ExternalCompanyId") external_id,
           min(i."ExternalId") industry
    from gauge g
    join "Companies" c on c."SymbolIsin" is not null and upper(trim(c."SymbolIsin")) = g.isin
    left join "Industries" i on i."Id" = c."IndustryId"
    group by 1
),
month_sales as (
    select co.ticker_id, r."PeriodEnd" month_end, coalesce(sum(l."SalesAmount"), 0) sales
    from company co
    join "MonthlyReports" r on r."ProviderName" = co.provider and r."ExternalCompanyId" = co.external_id
        and r."IsAccepted" and (r."OutputType" = 0 or r."OutputType" is null)
    join "MonthlyReportLineItems" l on l."MonthlyReportId" = r."Id"
    where co.matches = 1
    group by 1, 2
),
months_ranked as (
    select *, row_number() over (partition by ticker_id order by month_end desc) rn,
           month_end - lag(month_end) over (partition by ticker_id order by month_end) gap_days
    from month_sales
),
month_summary as (
    select ticker_id, count(*) filter (where rn <= 16) months, max(month_end) latest_month,
           coalesce(bool_and(gap_days <= 35) filter (where rn <= 15), true) consecutive,
           sum(sales) filter (where rn <= 4) recent4, sum(sales) filter (where rn between 13 and 16) comparison4
    from months_ranked
    group by 1
),
statements as (
    select s."TickerId" ticker_id, s."Id" id, s."PeriodEndingDate" period_end,
           row_number() over (partition by s."TickerId" order by s."PeriodEndingDate" desc, s."FiscalMonth" desc) rn
    from cyclicalwaves."IncomeStatements" s
    where s."TickerId" in (select ticker_id from gauge)
),
quarters as (
    select st.ticker_id, st.period_end, st.rn,
           coalesce(max(v."Quarter") filter (where v."LineDefinitionId" = 44),
                    max(v."Quarter") filter (where v."LineDefinitionId" = 46),
                    max(v."Quarter") filter (where v."LineDefinitionId" = 124),
                    max(v."Quarter") filter (where v."LineDefinitionId" = 412),
                    max(v."Quarter") filter (where v."LineDefinitionId" = 599)) revenue,
           max(v."Quarter") filter (where v."LineDefinitionId" = 66) net_profit
    from statements st
    left join cyclicalwaves."IncomeStatementValues" v on v."StatementId" = st.id
        and v."LineDefinitionId" in (44, 46, 124, 412, 599, 66)
    where st.rn <= 8
    group by 1, 2, 3
),
quarter_summary as (
    select ticker_id, count(*) quarters,
           bool_and(revenue > 0 and net_profit is not null) all_usable,
           coalesce(bool_and(gap_days <= 100) filter (where rn <= 7), true) consecutive,
           sum(net_profit) filter (where rn <= 4) profit4,
           percentile_cont(0.5) within group (order by (net_profit / nullif(revenue, 0))::float8) median_margin
    from (select *, period_end - lag(period_end) over (partition by ticker_id order by period_end, rn desc) gap_days
          from quarters) q
    group by 1
),
per_ticker as (
    select g.ticker,
           coalesce(ms.months, 0) >= 16 and coalesce(ms.consecutive, false) has_16_months,
           coalesce(qs.quarters, 0) = 8 and coalesce(qs.all_usable, false) and coalesce(qs.consecutive, false) has_8_quarters,
           case
               when co.ticker_id is null or co.matches <> 1 then 'company_unmatched'
               when co.industry = '31' then 'excluded_industry'
               when coalesce(ms.months, 0) < 16 or not ms.consecutive then 'insufficient_monthly_sales'
               when current_date - ms.latest_month > 75 then 'stale_monthly_sales'
               when ms.comparison4 <= 0 then 'no_comparison_sales'
               when ms.recent4 / ms.comparison4 < 0.5 or ms.recent4 / ms.comparison4 > 4 then 'growth_out_of_range'
               when coalesce(qs.quarters, 0) < 8 or not qs.all_usable or not qs.consecutive then 'insufficient_quarters'
               else 'estimated'
           end reason,
           g.ps_close, g.pe_close, qs.profit4, qs.median_margin
    from gauge g
    left join company co on co.ticker_id = g.ticker_id
    left join month_summary ms on ms.ticker_id = g.ticker_id
    left join quarter_summary qs on qs.ticker_id = g.ticker_id
)
select coalesce(reason, '(all gauge tickers)') reason,
       count(*) tickers,
       count(*) filter (where has_16_months) with_16_months,
       count(*) filter (where has_8_quarters) with_8_quarters,
       count(*) filter (where has_16_months and has_8_quarters) with_both,
       count(*) filter (where reason = 'estimated' and ps_close > 0) forward_ps_shown,
       count(*) filter (where reason = 'estimated' and pe_close > 0 and profit4 > 0 and median_margin > 0) forward_pe_shown
from per_ticker
group by rollup (reason)
order by reason nulls first;
