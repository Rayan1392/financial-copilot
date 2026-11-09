-- Feature 156 task 1.1. Read-only. Uses the published definition of NoavaranEligibleCompanies
-- because the feature141_app_reader role cannot read the view itself.
\pset pager off
SHOW default_transaction_read_only;
SELECT current_user;
\echo == column coverage
WITH eligible AS (SELECT "Id","ExternalCompanyId","Name","Ticker","TseSymbol","CompanySymbol" FROM "Companies"
  WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0
    AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5'))
SELECT count(*) total, count(NULLIF(btrim("Ticker"),'')) ticker, count(NULLIF(btrim("TseSymbol"),'')) tse, count(NULLIF(btrim("CompanySymbol"),'')) company_symbol,
  count(*) FILTER (WHERE "TseSymbol" IS DISTINCT FROM "CompanySymbol") tse_differs FROM eligible;
\echo == sample of tickers
WITH eligible AS (SELECT "Id","ExternalCompanyId","Name","Ticker","TseSymbol","CompanySymbol" FROM "Companies"
  WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0
    AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5'))
SELECT "ExternalCompanyId","Name","Ticker","TseSymbol","CompanySymbol" FROM eligible
WHERE "CompanySymbol" IN ('اخابر','کگهر','کگل','وبملت','فولاد','دی') OR "TseSymbol" IN ('اخابر','کگهر','کگل','وبملت','فولاد','دی') ORDER BY "CompanySymbol";
\echo == rows where the two symbol columns differ (first 20)
WITH eligible AS (SELECT "Id","ExternalCompanyId","Name","Ticker","TseSymbol","CompanySymbol" FROM "Companies"
  WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0
    AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5'))
SELECT "ExternalCompanyId","TseSymbol","CompanySymbol" FROM eligible WHERE "TseSymbol" IS DISTINCT FROM "CompanySymbol" ORDER BY 2 LIMIT 20;
\echo == compact keys shared by more than one company
WITH eligible AS (SELECT "Id","ExternalCompanyId","Name","Ticker","TseSymbol","CompanySymbol" FROM "Companies"
  WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0
    AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5')), keys AS (SELECT DISTINCT "Id", translate(replace(replace(lower(btrim(v)),' ',''),U&'C',''),'كيى','کیی') k, v
  FROM eligible, LATERAL (VALUES ("TseSymbol"),("CompanySymbol")) t(v) WHERE NULLIF(btrim(v),'') IS NOT NULL)
SELECT k, count(DISTINCT "Id") companies, string_agg(DISTINCT v, ' | ') FROM keys GROUP BY k HAVING count(DISTINCT "Id")>1 ORDER BY k;
\echo == eligible keys equal to common words
WITH eligible AS (SELECT "Id","ExternalCompanyId","Name","Ticker","TseSymbol","CompanySymbol" FROM "Companies"
  WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0
    AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5')), keys AS (SELECT DISTINCT "Id", translate(replace(replace(lower(btrim(v)),' ',''),U&'C',''),'كيى','کیی') k, v
  FROM eligible, LATERAL (VALUES ("TseSymbol"),("CompanySymbol")) t(v) WHERE NULLIF(btrim(v),'') IS NOT NULL)
SELECT k, string_agg(DISTINCT e."Name", ' | ') FROM keys JOIN eligible e USING ("Id") WHERE k = ANY(ARRAY['سلام','درود','مرسی','ممنون','متشکرم','بله','نه','خیر','باشه','اوکی','تست','خوبی','چطوری','کمک','راهنما','شروع','نماد','سهم','سهام','بازار','بورس','قیمت','فروش','سود','گزارش','تحلیل','شاخص','خرید','صنعت','پول','دلار','طلا','سکه','فروردین','اردیبهشت','خرداد','تیر','مرداد','شهریور','مهر','آبان','آذر','دی','بهمن','اسفند','امروز','فردا','دیروز','ماهانه','فصلی','ترازنامه','چارت','نمودار','روند','جدول','گیج','فهرست','خلاصه']) GROUP BY k ORDER BY k;
\echo == longest key length
WITH eligible AS (SELECT "Id","ExternalCompanyId","Name","Ticker","TseSymbol","CompanySymbol" FROM "Companies"
  WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0
    AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5')), keys AS (SELECT DISTINCT "Id", translate(replace(replace(lower(btrim(v)),' ',''),U&'C',''),'كيى','کیی') k, v
  FROM eligible, LATERAL (VALUES ("TseSymbol"),("CompanySymbol")) t(v) WHERE NULLIF(btrim(v),'') IS NOT NULL)
SELECT max(length(k)) FROM keys;
\echo == keys of 2 characters or fewer
WITH eligible AS (SELECT "Id","ExternalCompanyId","Name","Ticker","TseSymbol","CompanySymbol" FROM "Companies"
  WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0
    AND "MarketId" IN ('037c69ad-f519-419f-ae62-59003b6b2428','a3ccb30a-caed-4f26-a84a-ac0eb8c78c76','86c05022-632c-44cd-96c9-5c4f58c51ef5')), keys AS (SELECT DISTINCT "Id", translate(replace(replace(lower(btrim(v)),' ',''),U&'C',''),'كيى','کیی') k, v
  FROM eligible, LATERAL (VALUES ("TseSymbol"),("CompanySymbol")) t(v) WHERE NULLIF(btrim(v),'') IS NOT NULL)
SELECT k, v FROM keys WHERE length(k)<=2 ORDER BY k;
