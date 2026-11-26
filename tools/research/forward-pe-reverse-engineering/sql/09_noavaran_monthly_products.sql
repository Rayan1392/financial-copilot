-- Product-level monthly sales (quantity, rate, amount) for the sample symbols; month itself, accepted revision.
select c."TseSymbol" ticker, r."PeriodEnd" period_end, l."ProductCode" product_code, l."Title" title, l."Unit" unit,
       l."SalesQuantity" sales_quantity, l."SalesRate" sales_rate, l."SalesAmount" sales_amount_million_rial
from "MonthlyReports" r
join "Companies" c on c."ProviderName" = r."ProviderName" and c."ExternalCompanyId" = r."ExternalCompanyId"
join "MonthlyReportLineItems" l on l."MonthlyReportId" = r."Id"
where r."ProviderName" = 'NoavaranCurrentApi' and r."RevisionStatus" = 'Accepted' and r."OutputType" = 0
  and c."TseSymbol" in ('اخشان','فاذر','ارفع','اخابر','شگل','کگهر','خزامیا','فملی','فولاد','کگل') and r."PeriodEnd" >= '2025-10-01'
order by 1, 2, 3;
