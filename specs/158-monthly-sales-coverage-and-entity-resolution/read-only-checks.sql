-- Feature 158 read-only checks. Not executed yet: they need a read-only connection from the owner.
-- SELECT only. Keep connection details out of this file, of logs, and of reports.
-- Run with:
--   PGOPTIONS='-c default_transaction_read_only=on -c statement_timeout=30000' psql "$DSN" -v ON_ERROR_STOP=1 -f read-only-checks.sql
-- Confirm on first use: the "Failed" and "Completed" status strings and ExternalReference = company id come from code.
--
-- C2 cause labels:
--   A catalog gap; B outside eligible scope, explain only; C no monthly route, distinct reply;
--   D catalog refresh; E review the new report type; F resolver picks the wrong row (BUG-011);
--   G data present, investigate the turn; H snapshot backfill; I/J ingestion gap;
--   K vendor returned nothing, retries continue.
-- C2 also returns:
--   category_fa: the reply category for label C (Feature 158 D4).
--   resolver_today: approximate result of CanonicalQueryEntityResolver's exact steps before Feature 158.
--     It ignores the exact company-name step and fuzzy matching. ambiguous_* values show why a symbol
--     is reported ambiguous today.
--   other_symbol_rows and noavaran_rows: why label F or ambiguity applies. Label F means another provider's
--     row carries the symbol, so ResolveBySymbolAsync can pick it (Ticker first; otherwise table order).

-- C1: every catalog row per ticker (duplicates for BUG-008 and BUG-010)
WITH t(s) AS (VALUES ('فسبزوار'),('دسبحا'),('تابان'),('سابیک'),('غشکو'),('ثجنوب'),('والبر'),('ولساپا'),('ومعادن'),('ثهام'),('فملی'),('فولاد'),('آینده'),('فسوژ'),('دسبحان')),
c AS (
  SELECT co.*,
         translate(replace(btrim(co."Ticker"),U&'\200C',''),'يك','یک') kt,
         translate(replace(btrim(co."TseSymbol"),U&'\200C',''),'يك','یک') ks,
         translate(replace(btrim(co."CompanySymbol"),U&'\200C',''),'يك','یک') kc
  FROM "Companies" co)
SELECT t.s, c."ProviderName", c."ExternalCompanyId", c."Name", c."Ticker", c."TseSymbol", c."CompanySymbol",
       c."PrecedencyRight", m."Name" market, c."ReportingType", c."FundTypeTitle",
       (c."ProviderName"='NoavaranCurrentApi' AND c."PrecedencyRight"=0
        AND c."MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5')) eligible
FROM t JOIN c ON t.s IN (c.kt,c.ks,c.kc)
LEFT JOIN "Markets" m ON m."Id"=c."MarketId"
ORDER BY t.s, eligible DESC, c."ProviderName"
LIMIT 300;

-- C2: per-ticker cause label
WITH t(s) AS (VALUES ('فسبزوار'),('دسبحا'),('تابان'),('سابیک'),('غشکو'),('ثجنوب'),('والبر'),('ولساپا'),('ومعادن'),('ثهام'),('فملی'),('فولاد')),
c AS (
  SELECT co.*,
         translate(replace(btrim(co."Ticker"),U&'\200C',''),'يك','یک') kt,
         translate(replace(btrim(co."TseSymbol"),U&'\200C',''),'يك','یک') ks,
         translate(replace(btrim(co."CompanySymbol"),U&'\200C',''),'يك','یک') kc,
         (co."ProviderName"='NoavaranCurrentApi' AND co."PrecedencyRight"=0
          AND co."MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5')) elig
  FROM "Companies" co),
pick AS (
  SELECT DISTINCT ON (t.s) t.s, c."Id", c."ExternalCompanyId", c."ReportingType", c.elig
  FROM t JOIN c ON t.s IN (c.ks,c.kc)
  WHERE c."ProviderName"='NoavaranCurrentApi'
  ORDER BY t.s, c.elig DESC, c."ExternalCompanyId"),
oth AS (
  SELECT t.s,
         count(c."Id") FILTER (WHERE c."ProviderName"='NoavaranCurrentApi' AND t.s IN (c.ks,c.kc)) noavaran_rows,
         count(c."Id") FILTER (WHERE c.elig AND t.s IN (c.ks,c.kc)) eligible_rows,
         count(c."Id") FILTER (WHERE c."ProviderName"='NoavaranCurrentApi' AND t.s=c.kt) noavaran_ticker_rows,
         count(c."Id") FILTER (WHERE c."ProviderName"<>'NoavaranCurrentApi' AND t.s=c.kt) other_ticker_rows,
         count(c."Id") FILTER (WHERE c."ProviderName"<>'NoavaranCurrentApi') other_symbol_rows
  FROM t LEFT JOIN c ON t.s IN (c.kt,c.ks,c.kc) GROUP BY t.s)
SELECT o.s, p."ExternalCompanyId", p."ReportingType", p.elig,
       o.noavaran_rows, o.eligible_rows, o.other_ticker_rows, o.other_symbol_rows,
       CASE p."ReportingType" WHEN 1000001 THEN 'ساختمانی' WHEN 1000002 THEN 'سرمایه‌گذاری و هلدینگ' WHEN 1000003 THEN 'بانکی'
            WHEN 1000004 THEN 'لیزینگی' WHEN 1000006 THEN 'بیمه‌ای' WHEN 1000007 THEN 'حمل و نقل دریایی'
            WHEN 1000009 THEN 'تأمین سرمایه' END category_fa,
       CASE WHEN o.noavaran_ticker_rows>1 THEN 'ambiguous_ticker'
            WHEN o.noavaran_ticker_rows=1 THEN 'resolved_ticker'
            WHEN o.other_ticker_rows>1 THEN 'ambiguous_ticker'
            WHEN o.other_ticker_rows=1 THEN 'resolved_other_provider'
            WHEN o.noavaran_rows>1 THEN 'ambiguous_alias'
            WHEN o.noavaran_rows=1 THEN 'resolved_alias'
            WHEN o.other_symbol_rows>1 THEN 'ambiguous_other_providers'
            WHEN o.other_symbol_rows=1 THEN 'resolved_other_provider'
            ELSE 'no_exact_match' END resolver_today,
       sn.n snapshots, sn.latest, r.n accepted_reports, r.latest_period,
       u.runs, u.failed, u.completed_zero, u.last_error,
       CASE WHEN p."Id" IS NULL THEN 'A_no_noavaran_row'
            WHEN NOT p.elig THEN 'B_outside_scope'
            WHEN p."ReportingType" IN (1000001,1000002,1000003,1000004,1000006,1000007,1000009) THEN 'C_no_monthly_route'
            WHEN p."ReportingType" IS NULL THEN 'D_type_missing'
            WHEN p."ReportingType" NOT IN (1000000,1000005,1000008) THEN 'E_type_unknown'
            WHEN sn.n>0 AND o.other_symbol_rows>0 THEN 'F_resolver_wrong_row'
            WHEN sn.n>0 THEN 'G_data_present_recheck_routing'
            WHEN r.n>0 THEN 'H_reports_without_snapshot'
            WHEN u.runs=0 THEN 'I_never_attempted'
            WHEN u.failed>0 THEN 'J_ingestion_failing'
            ELSE 'K_vendor_returned_nothing' END cause
FROM oth o
LEFT JOIN pick p ON p.s=o.s
LEFT JOIN LATERAL (
  SELECT count(*) n, max(x."ReportYear"*100+x."ReportMonth") latest
  FROM "CompanyMonthlyActivityTrendSnapshots" x
  WHERE x."ExternalCompanyId"=p."ExternalCompanyId") sn ON true
LEFT JOIN LATERAL (
  SELECT count(*) n, max(m."PeriodStart") latest_period
  FROM "MonthlyReports" m
  WHERE m."ProviderName"='NoavaranCurrentApi' AND m."ExternalCompanyId"=p."ExternalCompanyId" AND m."IsAccepted") r ON true
LEFT JOIN LATERAL (
  SELECT count(*) runs,
         count(*) FILTER (WHERE z."Status"='Failed') failed,
         count(*) FILTER (WHERE z."Status"='Completed' AND z."ProcessedRecords"=0) completed_zero,
         left(max(z."ErrorMessage"),160) last_error
  FROM "ProviderSyncRuns" z
  WHERE z."Dataset"='MonthlyProductionSales' AND z."ExternalReference"=p."ExternalCompanyId" AND z."RequestedAt">now()-interval '365 days') u ON true
ORDER BY o.s
LIMIT 50;

-- C3: population reach
WITH c AS (
  SELECT co.*,
         translate(replace(btrim(co."Ticker"),U&'\200C',''),'يك','یک') kt,
         translate(replace(btrim(co."TseSymbol"),U&'\200C',''),'يك','یک') ks,
         translate(replace(btrim(co."CompanySymbol"),U&'\200C',''),'يك','یک') kc
  FROM "Companies" co),
e AS (
  SELECT * FROM c
  WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0
    AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5'))
SELECT 'eligible_by_type' metric, coalesce("ReportingType"::text,'null') key, count(*) n,
       count(*) FILTER (WHERE EXISTS (SELECT 1 FROM "CompanyMonthlyActivityTrendSnapshots" s WHERE s."ExternalCompanyId"=e."ExternalCompanyId")) with_snapshot
FROM e GROUP BY 2
UNION ALL
SELECT 'ambiguous_on_ticker','ge2_other_rows',count(*),NULL FROM e
WHERE (SELECT count(*) FROM c o WHERE o."ProviderName"<>'NoavaranCurrentApi' AND o.kt IN (e.ks,e.kc))>=2
UNION ALL
SELECT 'one_other_ticker_row','eq1_other_row',count(*),NULL FROM e
WHERE (SELECT count(*) FROM c o WHERE o."ProviderName"<>'NoavaranCurrentApi' AND o.kt IN (e.ks,e.kc))=1;
