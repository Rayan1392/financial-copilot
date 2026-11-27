-- Industry (Noavaran external id) of every reference symbol, for the excluded-industry guard of the estimator fixtures.
select c."TseSymbol" ticker, c."SymbolIsin" symbol_isin, i."ExternalId" industry_external_id, i."Name" industry_name
from "Companies" c left join "Industries" i on i."Id" = c."IndustryId"
where c."ProviderName" = 'NoavaranCurrentApi'
  and c."TseSymbol" in ('اخشان','فاذر','ارفع','اخابر','شگل','کگهر','خزامیا','فملی','فولاد','کگل',
                        'کچاد','فخوز','فایرا','فگستر','آپ','اروند','کطبس','شکلر','شپنا','بمپنا','اسیاتک','ستران','سمازن','حفارس','حفاری')
order by 1;
