-- Monthly sales (month itself) for every company that has a forward P/S snapshot. Accepted revisions only.
select c."TseSymbol" ticker, r."ReportType" report_type, r."PeriodEnd" period_end,
       min(r."ProviderPublishedAtUtc") provider_published_at, sum(l."SalesAmount") sales_million_rial
from "MonthlyReports" r
join "Companies" c on c."ProviderName" = r."ProviderName" and c."ExternalCompanyId" = r."ExternalCompanyId"
join "MonthlyReportLineItems" l on l."MonthlyReportId" = r."Id"
where r."ProviderName" = 'NoavaranCurrentApi' and r."RevisionStatus" = 'Accepted'
  and (r."OutputType" = 0 or r."OutputType" is null)
  and c."Id" in (select "CompanyId" from "CompanyPsGaugeSnapshots")
group by 1, 2, 3 order by 1, 3;
