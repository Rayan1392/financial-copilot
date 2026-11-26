-- Read-only sample of Feature 148 latest-state rows (local development database, not staging).
-- Value = sign * Low / 10^scale; valid while High and Middle are zero (checked in the last query).
\pset pager off
\echo '== gauge rows for the spec tickers =='
select t."Ticker", t."Enticker", d."GaugeType", d."A",d."B",d."C",d."D",d."E",d."F",
  case when d."CloseSign" is null then null else d."CloseSign"*d."CloseLow"/power(10,d."CloseScale") end as close,
  d."StartSign"*d."StartLow"/power(10,d."StartScale") as start, d."MinSign"*d."MinLow"/power(10,d."MinScale") as min,
  d."AvgSign"*d."AvgLow"/power(10,d."AvgScale") as avg, d."MaxSign"*d."MaxLow"/power(10,d."MaxScale") as max,
  d."EndSign"*d."EndLow"/power(10,d."EndScale") as "end", d."Status", d."FetchedAtUtc", a."HasPe", a."HasPs"
from cyclicalwaves."Tickers" t join cyclicalwaves."GaugeDetails" d on d."TickerId"=t."Id"
left join cyclicalwaves."GaugeAvailability" a on a."TickerId"=t."Id"
where t."Ticker" in ('شاراک','کگهر','اخابر','فولاد') order by 1,3;
\echo '== equilibrium rows for the spec tickers =='
select t."Ticker", r."HasRange", r."MinSign"*r."MinLow"/power(10,r."MinScale") as min, r."AvgSign"*r."AvgLow"/power(10,r."AvgScale") as avg,
  r."MaxSign"*r."MaxLow"/power(10,r."MaxScale") as max, r."DetailsFetchedAtUtc"
from cyclicalwaves."Tickers" t join cyclicalwaves."EquilibriumRanges" r on r."TickerId"=t."Id"
where t."Ticker" in ('شاراک','کگهر','اخابر','فولاد') order by 1;
\echo '== null close and null status counts =='
select "GaugeType", count(*) total, count(*) filter (where "CloseSign" is null) null_close,
  count(*) filter (where "Status" is null) null_status, count(*) filter (where "A"+"B"+"C"+"D"+"E"+"F"=0) zero_total
from cyclicalwaves."GaugeDetails" group by 1;
\echo '== negative equilibrium bounds =='
select count(*) filter (where "MinSign"=-1) neg_min, count(*) filter (where "AvgSign"=-1) neg_avg, count(*) filter (where "MaxSign"=-1) neg_max
from cyclicalwaves."EquilibriumRanges" where "HasRange";
select t."Ticker", r."MinSign"*r."MinLow"/power(10,r."MinScale") as min, r."AvgSign"*r."AvgLow"/power(10,r."AvgScale") as avg, r."MaxSign"*r."MaxLow"/power(10,r."MaxScale") as max
from cyclicalwaves."Tickers" t join cyclicalwaves."EquilibriumRanges" r on r."TickerId"=t."Id" where r."HasRange" and r."MinSign"=-1 limit 3;
\echo '== avg position inside min..max (asymmetric averages) =='
select count(*) total, count(*) filter (where abs(("AvgSign"*"AvgLow"/power(10,"AvgScale")) - (("MinSign"*"MinLow"/power(10,"MinScale")) + ("MaxSign"*"MaxLow"/power(10,"MaxScale")))/2) > 0.0001) asymmetric
from cyclicalwaves."GaugeDetails";
\echo '== degenerate axes (not start<min<avg<max<end) =='
select count(*) from cyclicalwaves."GaugeDetails" where not (
 "StartSign"*"StartLow"/power(10,"StartScale") < "MinSign"*"MinLow"/power(10,"MinScale") and
 "MinSign"*"MinLow"/power(10,"MinScale") < "AvgSign"*"AvgLow"/power(10,"AvgScale") and
 "AvgSign"*"AvgLow"/power(10,"AvgScale") < "MaxSign"*"MaxLow"/power(10,"MaxScale") and
 "MaxSign"*"MaxLow"/power(10,"MaxScale") < "EndSign"*"EndLow"/power(10,"EndScale"));
\echo '== duplicate normalized tickers / ISINs among active rows (ambiguity) =='
select 'ticker' kind, count(*) from (select "NormalizedTicker" from cyclicalwaves."Tickers" where "IsActive" group by 1 having count(*)>1) x
union all select 'isin', count(*) from (select upper(trim("Enticker")) from cyclicalwaves."Tickers" where "IsActive" and "Enticker" is not null group by 1 having count(*)>1) y;
\echo '== active tickers without an ISIN, and Noavaran companies whose ISIN has no v2 ticker (coverage misses) =='
select count(*) filter (where "Enticker" is null) no_isin from cyclicalwaves."Tickers" where "IsActive";
\echo '== large-number guard: rows needing High/Middle chunks =='
select count(*) from cyclicalwaves."GaugeDetails" where "StartHigh"<>0 or "StartMiddle"<>0 or "EndHigh"<>0 or "EndMiddle"<>0;
