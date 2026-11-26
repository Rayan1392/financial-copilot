-- TahlilApp v1 API: TTM and forward P/S per company and observation date (full precision).
select c."TseSymbol" ticker, c."ExternalCompanyId" company_id, c."InstrumentCode" ins_code, c."ReportingType" reporting_type,
       s."ObservationDate" obs_date, s."TtmPsRatio" ttm_ps, s."ForwardPsRatio" forward_ps, s."GaugeClose" gauge_close,
       s."CurrentValuesFetchedAtUtc" fetched_at
from "CompanyPsGaugeSnapshots" s join "Companies" c on c."Id" = s."CompanyId"
order by 1, 5;
