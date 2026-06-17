Select * from public."Companies"
WHERE "CompanySymbol" LIKE N'%کچاد%'

select * from public."DerivedMetrics"
WHERE "ExternalCompanyId" = '3'
ORDER BY "PeriodEnd" DESC

SELECT * FROM public."MonthlyReports"
WHERE "ExternalCompanyId" = '1772'
ORDER BY "PeriodEnd" DESC
--Noavaran Amin: 2026-05-21  Sales: 19,448,328
--CyclicalWaves: Revenue: 19,448,328,000,000 - AVG_12M: 6,139,121,250,000
--DerivedMetrics: 25,587,449,250,000
SELECT * FROM public."MonthlyReportLineItems"
WHERE "MonthlyReportId" = '9e762be9-3c96-4aa6-945a-0cffe9c89e93'


SELECT "Id", "FinancialStatementId", "MetricCode", "Value"
	FROM public."FinancialStatementLineItems";


	SELECT * FROM public."FinancialStatements"
	WHERE "ExternalCompanyId" = '444'



	SELECT *
	--li."MetricCode", li."Value", fs."PeriodStart", fs."PeriodEnd"
FROM public."FinancialStatementLineItems" li
JOIN public."FinancialStatements" fs ON fs."Id" = li."FinancialStatementId"
WHERE fs."ProviderName" = 'CyclicalWaves'
  --AND fs."ExternalStatementId" LIKE '%6a1ea61b9b0e8d0e71022204%'
  AND li."MetricCode" IN (
    'GROSS_PROFIT_MARGIN', 'NET_PROFIT_MARGIN', 'OPERATING_PROFIT_MARGIN',
    'AVG_4Q_REVENUE', 'REVENUE', 'PE_RATIO', 'PS_RATIO'
  )
ORDER BY fs."PeriodEnd" DESC, li."MetricCode";


SELECT li."MetricCode", li."Value", fs."PeriodStart", fs."PeriodEnd"
FROM public."FinancialStatementLineItems" li
JOIN public."FinancialStatements" fs ON fs."Id" = li."FinancialStatementId"
WHERE fs."ProviderName" = 'CyclicalWaves'
  AND fs."ExternalStatementId" LIKE '%6a1ea61b9b0e8d0e71022204%'
  AND li."MetricCode" IN (
    'GROSS_PROFIT_MARGIN', 'NET_PROFIT_MARGIN', 'OPERATING_PROFIT_MARGIN',
    'AVG_4Q_REVENUE', 'REVENUE', 'PE_RATIO', 'PS_RATIO'
  )
ORDER BY fs."PeriodEnd" DESC, li."MetricCode";



SELECT dm."MetricCode", dm."Value", dm."PeriodStart", dm."PeriodEnd",
       dm."PeriodType", dm."ObservedAt", dm."WarningsJson"
FROM public."DerivedMetrics" dm
JOIN public."Symbols" s ON s."Id" = dm."SymbolId"
WHERE s."SymbolCode" = 'شغدیر'
  AND dm."MetricCode" IN (
    'GROSS_PROFIT_MARGIN', 'NET_PROFIT_MARGIN', 'OPERATING_PROFIT_MARGIN',
    'AVG_4Q_REVENUE', 'PE_TTM', 'PS_TTM', 'REVENUE'
  )
ORDER BY dm."MetricCode", dm."PeriodEnd" DESC;



SELECT "SourceDataset", "ExternalCompanyId", "RequestedAt",
       "ProcessedAt", "AttemptCount", "LastError"
FROM public."MetricRecalculationRequests"
WHERE "ExternalCompanyId" = '21772258644715569'   -- مقدار از کوئری 3
ORDER BY "RequestedAt" DESC
LIMIT 20;



-- ابتدا ExternalCompanyId شغدیر را پیدا کن
SELECT "ExternalCompanyId" 
FROM public."Companies"
WHERE "CompanySymbol" = 'شغدیر'
  AND "ProviderName" = 'NoavaranCurrentApi';
  --1772
  
-- وضعیت request را چک کن
SELECT "ProcessedAt", "AttemptCount", "LastError"
FROM public."MetricRecalculationRequests"
WHERE "ExternalReference" = '1772'
ORDER BY "RequestedAt" DESC
LIMIT 50;


SELECT dm."MetricCode", dm."Value", dm."PeriodStart", dm."PeriodEnd",
       dm."PeriodType", dm."ObservedAt", dm."WarningsJson"
FROM public."DerivedMetrics" dm
JOIN public."Companies" s ON s."ExtrnalCompanyId" = dm."ExtrnalCompanyId"
WHERE s."ExternalSymbolId" = 'شغدیر'
  AND dm."MetricCode" IN (
    'GROSS_PROFIT_MARGIN', 'NET_PROFIT_MARGIN', 'OPERATING_PROFIT_MARGIN',
    'AVG_4Q_REVENUE', 'PE_TTM', 'PS_TTM', 'REVENUE'
  )
ORDER BY dm."MetricCode", dm."PeriodEnd" DESC;



-- وضعیت Symbols برای شغدیر از همه providers
SELECT "Id", "ProviderName", "ExternalSymbolId", "SymbolCode", "CompanyId", "LastSynchronizedAt"
FROM public."Symbols"
WHERE "ExternalSymbolId" ILIKE '%شغدیر%'
   OR "SymbolCode" ILIKE '%شغدیر%'
   OR "ExternalSymbolId" ILIKE '%PGDR%'
   OR "SymbolCode" ILIKE '%PGDR%';

-- DerivedMetrics برای شغدیر از همه Symbols
SELECT dm."MetricCode", dm."Value", dm."PeriodEnd", s."ProviderName", 
       s."SymbolCode", s."ExternalSymbolId"
FROM public."DerivedMetrics" dm
JOIN public."Symbols" s ON s."Id" = dm."SymbolId"
JOIN public."Companies" c ON c."Id" = s."CompanyId"
WHERE c."CompanySymbol" = 'شغدیر'
ORDER BY dm."MetricCode", dm."PeriodEnd" DESC;


-- آیا CyclicalWaves Symbol row برای شغدیر وجود دارد؟
SELECT "Id", "ProviderName", "ExternalSymbolId", "SymbolCode", "CompanyId"
FROM public."Symbols"
WHERE "CompanyId" = '04f7e22c-34c3-4133-86cd-8248f0de5f71';  -- CompanyId شغدیر



-- DerivedMetrics به کدام SymbolId وصل است؟
SELECT dm."MetricCode", dm."Value", dm."PeriodEnd", 
       s."ProviderName", s."SymbolCode", s."ExternalSymbolId"
FROM public."DerivedMetrics" dm
JOIN public."Symbols" s ON s."Id" = dm."SymbolId"
WHERE s."CompanyId" = '04f7e22c-34c3-4133-86cd-8248f0de5f71'
  AND dm."MetricCode" IN ('PE_TTM', 'PS_TTM', 'GROSS_PROFIT_MARGIN')
ORDER BY dm."MetricCode", dm."PeriodEnd" DESC;


select * from public."DerivedMetrics"
WHERE "MetricCode" = 'GROSS_PROFIT_MARGIN'

-- آیا request ای در outbox هست؟
SELECT "Id", "SourceDataset", "ExternalReference", "RequestedAt", 
       "ProcessedAt", "AttemptCount", "LastError"
FROM public."MetricRecalculationRequests"
WHERE "SourceDataset" = 'FinancialStatements'
ORDER BY "RequestedAt" DESC
LIMIT 10;

-- آیا اصلاً رکوردی در FinancialStatements برای CyclicalWaves هست؟
SELECT "ExternalCompanyId", "ExternalStatementId", "ProviderName", 
       "PeriodEnd", "LastSynchronizedAt"
FROM public."FinancialStatements"
WHERE "ProviderName" = 'CyclicalWaves'
LIMIT 5;

-- آیا line item ای برای GROSS_PROFIT_MARGIN در FinancialStatementLineItems هست؟
SELECT li."MetricCode", li."Value", fs."ExternalCompanyId", fs."PeriodEnd"
FROM public."FinancialStatementLineItems" li
JOIN public."FinancialStatements" fs ON fs."Id" = li."FinancialStatementId"
WHERE li."MetricCode" = 'GROSS_PROFIT_MARGIN'
AND fs."ExternalCompanyId" = '1772'

-- تأیید اینکه request پردازش شد
SELECT "ProcessedAt", "AttemptCount", "LastError"
FROM public."MetricRecalculationRequests"
WHERE "ExternalReference" = '1772'
ORDER BY "RequestedAt" DESC
LIMIT 1;

-- تأیید اینکه DerivedMetrics نوشته شد
SELECT "MetricCode", "Value", "PeriodStart", "PeriodEnd"
FROM public."DerivedMetrics" dm
JOIN public."Symbols" s ON s."Id" = dm."SymbolId"
WHERE s."CompanyId" = '04f7e22c-34c3-4133-86cd-8248f0de5f71'
  AND dm."MetricCode" = 'GROSS_PROFIT_MARGIN';



SELECT dm."MetricCode", dm."Value", dm."PeriodEnd"
FROM public."DerivedMetrics" dm
JOIN public."Symbols" s ON s."Id" = dm."SymbolId"
WHERE s."CompanyId" = '04f7e22c-34c3-4133-86cd-8248f0de5f71'
  AND dm."MetricCode" IN ('GROSS_PROFIT_MARGIN', 'NET_PROFIT_MARGIN', 'OPERATING_PROFIT_MARGIN')
ORDER BY dm."MetricCode", dm."PeriodEnd" DESC;

select * from public."MonthlyReports"
where "ProviderName" ='CyclicalWaves'
order by "LastSynchronizedAt" DESC

