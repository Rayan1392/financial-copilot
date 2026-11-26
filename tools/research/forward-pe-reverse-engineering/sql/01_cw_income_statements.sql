-- TahlilApp (CyclicalWaves) income statements: discrete quarter, YTD and TTM per line.
select t."Ticker" ticker, s."PeriodEndingDate" period_end, s."FiscalYear" fy, s."FiscalMonth" fm, s."Audited" audited,
       s."PublishDate"::date published, v."LineDefinitionId" line_id, d."NameEn" line_name,
       v."Quarter" quarter, v."YearToDate" ytd, v."TrailingYear" ttm
from cyclicalwaves."IncomeStatements" s
join cyclicalwaves."Tickers" t on t."Id" = s."TickerId"
join cyclicalwaves."IncomeStatementValues" v on v."StatementId" = s."Id"
join cyclicalwaves."IncomeStatementLineDefinitions" d on d."Id" = v."LineDefinitionId"
where t."Ticker" in ('اخشان','فاذر','ارفع','اخابر','شگل','کگهر','خزامیا','فملی','فولاد','کگل')
  and s."PeriodEndingDate" >= '2023-01-01'
order by 1, 2, v."SortOrder";
