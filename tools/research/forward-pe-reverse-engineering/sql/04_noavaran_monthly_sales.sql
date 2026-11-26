-- Monthly sales per company and month (Noavaran / Codal monthly activity reports).
-- ProductSales: OutputType 0 = the month itself (1 = fiscal-year cumulative, 3/4 = restated previous cumulative).
-- Only the Accepted revision of each month is used, so superseded and pending revisions are not double counted.
select c."TseSymbol" ticker, r."ReportType" report_type, r."OutputType" output_type, r."PeriodStart" period_start,
       r."PeriodEnd" period_end, r."PublishedAt" published_at, r."ProviderPublishedAtUtc" provider_published_at,
       r."RevisionStatus" revision_status, count(*) line_count, sum(l."SalesAmount") sales_amount_million_rial
from "MonthlyReports" r
join "Companies" c on c."ProviderName" = r."ProviderName" and c."ExternalCompanyId" = r."ExternalCompanyId"
join "MonthlyReportLineItems" l on l."MonthlyReportId" = r."Id"
where r."ProviderName" = 'NoavaranCurrentApi'
  and c."TseSymbol" in ('اخشان','فاذر','ارفع','اخابر','شگل','کگهر','خزامیا','فملی','فولاد','کگل')
  and r."RevisionStatus" = 'Accepted'
  and r."PeriodEnd" >= '2024-03-01'
group by 1, 2, 3, 4, 5, 6, 7, 8
order by 1, 5, 3;
