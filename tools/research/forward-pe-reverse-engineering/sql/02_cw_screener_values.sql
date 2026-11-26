-- TahlilApp screener values (current P/E, P/S, quarter and month sales/profit facts).
select t."Ticker" ticker, d."FieldKey" field_key, v."NumericValue" value, v."UpdatedAt" updated_at,
       m."LastMonthSaleDate" last_month_sale_date, m."LastQuarterDate" last_quarter_date
from cyclicalwaves."FinancialValues" v
join cyclicalwaves."Tickers" t on t."Id" = v."TickerId"
join cyclicalwaves."MetricDefinitions" d on d."Id" = v."MetricDefinitionId"
left join cyclicalwaves."ScreenerMetadata" m on m."TickerId" = t."Id"
where t."Ticker" in ('اخشان','فاذر','ارفع','اخابر','شگل','کگهر','خزامیا','فملی','فولاد','کگل')
order by 1, d."SortOrder";
