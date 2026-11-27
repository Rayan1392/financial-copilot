-- Out-of-sample validation set (third batch). TahlilApp gauge rows: current P/E and P/S "close" (decimal = Low * 10^-Scale, sign applied).
select t."Ticker" ticker, g."GaugeType" gauge, (case when g."CloseSign"<0 then -1 else 1 end) * g."CloseLow" / power(10::numeric, g."CloseScale") close_ratio,
       g."Status" status, g."FetchedAtUtc" fetched_at
from cyclicalwaves."GaugeDetails" g join cyclicalwaves."Tickers" t on t."Id" = g."TickerId"
where t."Ticker" in ('کچاد','فخوز','فایرا','فگستر','آپ','اروند','کطبس','شکلر','شپنا','بمپنا','اسیاتک','ستران','سمازن','حفارس','حفاری') order by 1, 2;
