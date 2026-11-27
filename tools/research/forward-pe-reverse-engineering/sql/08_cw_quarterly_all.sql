-- TahlilApp quarterly revenue and net profit for every ticker (discrete quarter values), used for cross-sectional tests.
select t."Ticker" ticker, s."PeriodEndingDate" period_end, s."FiscalYear" fy, s."FiscalMonth" fm, s."PublishDate"::date published,
       max(v."Quarter") filter (where v."LineDefinitionId" in (44,46,47,124,412,599)) revenue_q,
       max(v."Quarter") filter (where v."LineDefinitionId" = 66) net_profit_q,
       max(v."Quarter") filter (where v."LineDefinitionId" = 56) operating_profit_q,
       max(v."Quarter") filter (where v."LineDefinitionId" = 52) gross_profit_q,
       max(v."Quarter") filter (where v."LineDefinitionId" = 85) eps_q,
       max(v."Quarter") filter (where v."LineDefinitionId" = 86) capital
from cyclicalwaves."IncomeStatements" s
join cyclicalwaves."Tickers" t on t."Id" = s."TickerId"
join cyclicalwaves."IncomeStatementValues" v on v."StatementId" = s."Id"
where s."PeriodEndingDate" >= '2024-01-01'
group by 1, 2, 3, 4, 5 order by 1, 2;
