import { toPersianDigits } from "@/lib/format/persian";

const insightTypeNames: Record<string, string> = {
  MonthlyReportPublished: "انتشار گزارش ماهانه",
  MonthlySalesAnomaly: "ناهنجاری فروش ماهانه",
  MonthlyQualityRankingChange: "تغییر کیفیت گزارش ماهانه",
  PriceMovement: "حرکت قیمت",
  ComprehensiveAnalysisPublished: "انتشار تحلیل جامع",
  FinancialStatementPublished: "انتشار صورت مالی",
  CodalAnnouncementMatched: "تطبیق اطلاعیه کدال",
  DataFreshnessWarning: "هشدار به‌روز بودن داده",
  LargeTradeDetected: "شناسایی معامله بزرگ",
  OrderQueueChanged: "تغییر صف سفارش",
  BuyerSellerPowerChanged: "تغییر قدرت خریدار و فروشنده",
  RealMoneyFlowChanged: "تغییر جریان نقدینگی حقیقی",
  TradingVolumeAnomaly: "ناهنجاری حجم معاملات",
  TradingValueAnomaly: "ناهنجاری ارزش معاملات",
};

const severityNames: Record<string, string> = {
  Informational: "اطلاع‌رسانی",
  Notice: "قابل توجه",
  Important: "مهم",
  Critical: "بحرانی",
};

const providerNames: Record<string, string> = {
  NoavaranCurrentApi: "نوآوران امین",
  NoavaranArchiveSql: "بایگانی نوآوران امین",
  CyclicalWaves: "امواج چرخه‌ای",
  TsetmcWebService: "وب‌سرویس بورس",
  CodalDb: "پایگاه کدال",
  StockMarketDb: "پایگاه داده بازار",
};

const sourceEntityNames: Record<string, string> = {
  MonthlyReport: "گزارش ماهانه",
  MonthlyActivityTrendSnapshot: "روند فعالیت ماهانه",
  MonthlySalesQualityRankingSnapshot: "رتبه‌بندی کیفیت فروش ماهانه",
  MarketQuote: "مظنه بازار",
  ComprehensiveAnalysis: "تحلیل جامع",
  FinancialStatement: "صورت مالی",
  IncomeStatement: "صورت سود و زیان",
  BalanceSheet: "ترازنامه",
  CashFlowStatement: "صورت جریان وجوه نقد",
  MonthlyActivity: "فعالیت ماهانه",
  MonthlyProduction: "تولید ماهانه",
  SyncState: "وضعیت همگام‌سازی",
  MarketMicrostructureObservation: "مشاهده ریزساختار معاملات",
};

const evidenceNames: Record<string, string> = {
  report_period: "دوره گزارش",
  report_type: "نوع گزارش",
  "Report period": "دوره گزارش",
  "Report type": "نوع گزارش",
  latest_monthly_sales: "آخرین فروش ماهانه",
  "Latest monthly sales": "آخرین فروش ماهانه",
  twelve_month_average: "میانگین ۱۲ ماهه",
  "12-month average": "میانگین ۱۲ ماهه",
  sales_versus_average: "فروش نسبت به میانگین",
  "Sales versus average": "فروش نسبت به میانگین",
  current_quality_score: "امتیاز کیفیت فعلی",
  "Current quality score": "امتیاز کیفیت فعلی",
  previous_quality_score: "امتیاز کیفیت قبلی",
  "Previous quality score": "امتیاز کیفیت قبلی",
  quality_label: "برچسب کیفیت",
  "Quality label": "برچسب کیفیت",
  latest_price: "آخرین قیمت",
  "Latest price": "آخرین قیمت",
  daily_change: "تغییر روزانه",
  "Daily change": "تغییر روزانه",
  analysis_title: "عنوان تحلیل",
  "Analysis title": "عنوان تحلیل",
  author: "نویسنده",
  statement_type: "نوع صورت مالی",
  "Statement type": "نوع صورت مالی",
  period_type: "نوع دوره",
  "Period type": "نوع دوره",
  announcement_type: "نوع اطلاعیه",
  "Announcement type": "نوع اطلاعیه",
  source_checksum: "شناسه صحت منبع",
  "Source checksum": "شناسه صحت منبع",
  dataset: "مجموعه‌داده",
  Dataset: "مجموعه‌داده",
  last_successful_sync: "آخرین همگام‌سازی موفق",
  "Last successful sync": "آخرین همگام‌سازی موفق",
  detector_code: "نوع تشخیص",
  detector_version: "نسخه تشخیص",
  instrument_identity: "شناسه نماد",
  trading_date: "تاریخ معاملات",
  window: "بازه بررسی",
  source_event_identity: "شناسه رویداد منبع",
  calculated_at_utc: "زمان محاسبه",
  source_synced_at_utc: "زمان همگام‌سازی منبع",
  source_lag_seconds: "تأخیر منبع به ثانیه",
  market_session_state: "وضعیت جلسه معاملاتی",
  money_unit: "واحد ارزش",
  volume_unit: "واحد حجم",
  is_correction: "اصلاحیه",
  supersedes_source_event_identity: "رویداد جایگزین‌شده",
  largest_trade_value: "ارزش بزرگ‌ترین معامله",
  largest_trade_volume: "حجم بزرگ‌ترین معامله",
  trade_side: "سمت معامله",
  threshold: "آستانه",
  absolute_threshold: "آستانه مطلق",
  relative_threshold: "آستانه نسبی",
  baseline_median_value: "میانه مبنا",
  baseline_observations: "تعداد مشاهدات مبنا",
  buyer_power_ratio: "نسبت قدرت خریدار",
  buyer_power_threshold: "آستانه قدرت خریدار",
  real_buy_average_volume: "میانگین حجم خرید حقیقی",
  real_sell_average_volume: "میانگین حجم فروش حقیقی",
  real_buyer_count: "تعداد خریداران حقیقی",
  real_seller_count: "تعداد فروشندگان حقیقی",
  real_buy_value: "ارزش خرید حقیقی",
  real_sell_value: "ارزش فروش حقیقی",
  net_real_money_flow: "خالص جریان نقدینگی حقیقی",
  institutional_buy_value: "ارزش خرید حقوقی",
  institutional_sell_value: "ارزش فروش حقوقی",
  queue_side: "سمت صف",
  queue_value: "ارزش صف",
  queue_volume: "حجم صف",
  duration_seconds: "مدت صف به ثانیه",
  previous_queue_value: "ارزش قبلی صف",
  collection_confirmed: "جمع‌آوری تأیید شد",
  minimum_queue_value: "حداقل ارزش صف",
  minimum_duration_seconds: "حداقل مدت به ثانیه",
  hysteresis_ratio: "نسبت بازگشت‌پذیری",
  current_volume: "حجم فعلی",
  current_trading_value: "ارزش معاملات فعلی",
  baseline_median: "میانه حجم مبنا",
  ratio: "نسبت",
  baseline_lookback: "دوره مبنا",
  minimum_baseline_observations: "حداقل مشاهدات مبنا",
  threshold_ratio: "نسبت آستانه",
  rarity_percentile: "صدک کمیابی",
};

const valueNames: Record<string, string> = {
  above: "بالاتر از مبنا",
  below: "پایین‌تر از مبنا",
  up: "افزایش",
  down: "کاهش",
  improved: "بهبود یافته",
  deteriorated: "کاهش یافته",
  Buy: "خرید",
  Sell: "فروش",
  Unknown: "نامشخص",
  Trading: "در حال معامله",
  OutsideTrading: "خارج از زمان معاملات",
  MonthlyActivity: "فعالیت ماهانه",
  FinancialStatement: "صورت مالی",
  IncomeStatement: "صورت سود و زیان",
  BalanceSheet: "ترازنامه",
  CashFlowStatement: "صورت جریان وجوه نقد",
  MonthlyProduction: "تولید ماهانه",
  ProductSales: "تولید و فروش ماهانه",
  TwelveMonths: "۱۲ ماهه",
  ThreeMonths: "سه‌ماهه",
  Monthly: "ماهانه",
  "Strong report": "گزارش قوی",
  "Weak report": "گزارش ضعیف",
  rial: "ریال",
  share: "سهم",
  true: "بله",
  false: "خیر",
  unavailable: "در دسترس نیست",
  none: "موردی ندارد",
};

function hasLatinText(value: string) {
  return /[A-Za-z]/.test(value);
}

function localizeToken(value: string) {
  if (valueNames[value]) return valueNames[value];
  if (valueNames[value.toLowerCase()]) return valueNames[value.toLowerCase()];
  return value;
}

function fallbackTitle(insightType: string, symbol: string) {
  const names: Record<string, string> = {
    MonthlyReportPublished: `گزارش ماهانه ${symbol} منتشر شد`,
    MonthlySalesAnomaly: `ناهنجاری فروش ماهانه ${symbol}`,
    MonthlyQualityRankingChange: `تغییر کیفیت گزارش ماهانه ${symbol}`,
    PriceMovement: `حرکت شدید روزانه قیمت ${symbol}`,
    ComprehensiveAnalysisPublished: `تحلیل جامع جدیدی برای ${symbol} منتشر شد`,
    FinancialStatementPublished: `صورت مالی ${symbol} منتشر شد`,
    CodalAnnouncementMatched: `اطلاعیه کدال ${symbol} تطبیق داده شد`,
    DataFreshnessWarning: `هشدار به‌روز بودن داده‌های ${symbol}`,
    LargeTradeDetected: `معامله بزرگ در ${symbol} شناسایی شد`,
    OrderQueueChanged: `صف سفارش ${symbol} تغییر کرد`,
    BuyerSellerPowerChanged: `قدرت خریدار و فروشنده ${symbol} تغییر کرد`,
    RealMoneyFlowChanged: `جریان نقدینگی حقیقی ${symbol} تغییر کرد`,
    TradingVolumeAnomaly: `ناهنجاری حجم معاملات ${symbol}`,
    TradingValueAnomaly: `ناهنجاری ارزش معاملات ${symbol}`,
  };
  return names[insightType] ?? "رویداد جدید بازار";
}

function fallbackSummary(insightType: string, symbol: string) {
  const summaries: Record<string, string> = {
    MonthlyReportPublished: `گزارش جدید تولید و فروش ماهانه ${symbol} در دسترس است.`,
    MonthlySalesAnomaly: `فروش ماهانه ${symbol} نسبت به مبنا تغییر محسوسی داشته است.`,
    MonthlyQualityRankingChange: `امتیاز کیفیت گزارش ماهانه ${symbol} نسبت به دوره قبل تغییر کرده است.`,
    PriceMovement: `آخرین مظنه بازار ${symbol} تغییر روزانه محسوسی داشته است.`,
    ComprehensiveAnalysisPublished: `تحلیل جامع جدیدی برای ${symbol} در دسترس است.`,
    FinancialStatementPublished: `صورت مالی جدید ${symbol} در دسترس است.`,
    CodalAnnouncementMatched: `اطلاعیه کدال مرتبط با ${symbol} با یک هشدار فعال تطبیق داده شد.`,
    DataFreshnessWarning: `تازگی داده‌های ${symbol} نیازمند بررسی است.`,
    LargeTradeDetected: `یک معامله بزرگ برای ${symbol} شناسایی شده است.`,
    OrderQueueChanged: `وضعیت صف سفارش ${symbol} تغییر کرده است.`,
    BuyerSellerPowerChanged: `قدرت خریدار و فروشنده حقیقی ${symbol} تغییر کرده است.`,
    RealMoneyFlowChanged: `جریان نقدینگی حقیقی ${symbol} تغییر محسوسی داشته است.`,
    TradingVolumeAnomaly: `حجم معاملات ${symbol} نسبت به مبنای تاریخی ناهنجاری نشان می‌دهد.`,
    TradingValueAnomaly: `ارزش معاملات ${symbol} نسبت به مبنای تاریخی ناهنجاری نشان می‌دهد.`,
  };
  return summaries[insightType] ?? "جزئیات این رویداد در دسترس است.";
}

export function localizeInsightType(value: string) {
  return insightTypeNames[value] ?? "رویداد بازار";
}

export function localizeSeverity(value: string) {
  return severityNames[value] ?? "قابل توجه";
}

export function localizeProviderName(value: string) {
  if (providerNames[value]) return providerNames[value];
  return hasLatinText(value) ? "منبع داده" : value;
}

export function localizeSourceEntityType(value: string) {
  if (sourceEntityNames[value]) return sourceEntityNames[value];
  return hasLatinText(value) ? "موجودیت داده" : value;
}

export function localizeEvidenceLabel(value: string) {
  if (evidenceNames[value]) return evidenceNames[value];
  return hasLatinText(value) ? "شاخص" : value;
}

export function localizeInsightValue(value: string, label?: string) {
  if (/^\d{4}-\d{2}-\d{2}(?:\/.*)?$/.test(value)) return localizePeriod(value);
  if (/^\d{4}-\d{2}-\d{2}T/.test(value)) return formatShamsiDate(value);
  if (label && /(date|time|sync|period)/i.test(label) && /^\d{4}[-/]/.test(value)) {
    return localizePeriod(value);
  }
  const localized = localizeToken(value);
  return hasLatinText(localized) ? toPersianDigits(localized) : localized;
}

export function localizePeriod(value: string) {
  const localized = localizeToken(value);
  if (localized !== value) return localized;

  const [datePart, ...suffix] = value.split("/");
  if (/^\d{4}-\d{2}-\d{2}$/.test(datePart)) {
    const formatted = formatShamsiDate(`${datePart}T12:00:00Z`);
    return suffix.length > 0 ? `${formatted}/${toPersianDigits(suffix.join("/"))}` : formatted;
  }
  return toPersianDigits(value);
}

export function formatShamsiDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return toPersianDigits(value);
  return new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
    calendar: "persian",
    timeZone: "Asia/Tehran",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(date);
}

export function formatInsightScore(value: number) {
  return toPersianDigits(Number.isInteger(value) ? value.toString() : value.toFixed(1));
}

export function localizeInsightTitle(title: string, symbol: string, insightType: string) {
  const matchers: Array<[RegExp, (match: RegExpMatchArray) => string]> = [
    [/^(.+?) monthly report was published$/, (match) => `گزارش ماهانه ${match[1]} منتشر شد`],
    [
      /^(.+?) monthly report quality improved$/,
      (match) => `کیفیت گزارش ماهانه ${match[1]} بهبود یافت`,
    ],
    [
      /^(.+?) monthly report quality deteriorated$/,
      (match) => `کیفیت گزارش ماهانه ${match[1]} کاهش یافت`,
    ],
    [
      /^(.+?) monthly sales were materially above baseline$/,
      (match) => `فروش ماهانه ${match[1]} به‌طور محسوسی بالاتر از مبنا بود`,
    ],
    [
      /^(.+?) monthly sales were materially below baseline$/,
      (match) => `فروش ماهانه ${match[1]} به‌طور محسوسی پایین‌تر از مبنا بود`,
    ],
    [/^(.+?) had a large daily price move$/, (match) => `حرکت شدید روزانه قیمت ${match[1]}`],
    [
      /^New comprehensive analysis was published for (.+)$/,
      (match) => `تحلیل جامع جدیدی برای ${match[1]} منتشر شد`,
    ],
    [/^(.+?) financial statement was published$/, (match) => `صورت مالی ${match[1]} منتشر شد`],
    [
      /^Codal monthly announcement matched for (.+)$/,
      (match) => `اطلاعیه ماهانه کدال ${match[1]} تطبیق داده شد`,
    ],
    [/^Codal announcement matched for (.+)$/, (match) => `اطلاعیه کدال ${match[1]} تطبیق داده شد`],
    [/^(.+?) data may be stale$/, (match) => `داده‌های ${match[1]} ممکن است به‌روز نباشند`],
    [/^Large trade detected$/, () => `معامله بزرگ در ${symbol} شناسایی شد`],
    [/^Buyer power detected$/, () => `قدرت خریدار در ${symbol} شناسایی شد`],
    [/^Seller power detected$/, () => `قدرت فروشنده در ${symbol} شناسایی شد`],
    [/^Retail money inflow detected$/, () => `ورود نقدینگی حقیقی به ${symbol} شناسایی شد`],
    [/^Retail money outflow detected$/, () => `خروج نقدینگی حقیقی از ${symbol} شناسایی شد`],
    [
      /^(Buy|Sell) queue (formed|strengthened|weakened|collected|released)$/,
      (match) => {
        const side = match[1] === "Buy" ? "خرید" : "فروش";
        const verb =
          {
            formed: "تشکیل شد",
            strengthened: "تقویت شد",
            weakened: "تضعیف شد",
            collected: "جمع‌آوری شد",
            released: "آزاد شد",
          }[match[2]] ?? "تغییر کرد";
        return `صف ${side} ${verb}`;
      },
    ],
    [/^Trading volume anomaly detected$/, () => `ناهنجاری حجم معاملات ${symbol} شناسایی شد`],
    [/^Trading value anomaly detected$/, () => `ناهنجاری ارزش معاملات ${symbol} شناسایی شد`],
  ];
  for (const [pattern, localizer] of matchers) {
    const match = title.match(pattern);
    if (match) return localizer(match);
  }
  return hasLatinText(title) ? fallbackTitle(insightType, symbol) : title;
}

export function localizeInsightSummary(summary: string, symbol: string, insightType: string) {
  let match = summary.match(/^A new monthly production\/sales report is available for (.+)\.$/);
  if (match) return `گزارش جدید تولید و فروش ماهانه ${match[1]} در دسترس است.`;
  match = summary.match(
    /^Monthly sales quality score (improved|deteriorated) by ([+-]?[\d.]+) points versus the prior period\.$/,
  );
  if (match) {
    const direction = match[1] === "improved" ? "بهبود یافت" : "کاهش یافت";
    return `امتیاز کیفیت فروش ماهانه نسبت به دوره قبل به میزان ${toPersianDigits(match[2])} واحد ${direction}.`;
  }
  match = summary.match(
    /^Latest monthly sales were ([+-]?[\d.]+)% (above|below) the 12-month average\.$/,
  );
  if (match)
    return `آخرین فروش ماهانه ${toPersianDigits(match[1])}٪ ${match[2] === "above" ? "بالاتر از" : "پایین‌تر از"} میانگین ۱۲ ماهه بود.`;
  match = summary.match(/^Latest market quote moved ([+-]?[\d.]+)% (up|down)\.$/);
  if (match)
    return `آخرین مظنه بازار ${toPersianDigits(match[1])}٪ ${match[2] === "up" ? "افزایش" : "کاهش"} داشت.`;
  match = summary.match(/^(.+) for period ending (.+) is available\.$/);
  if (match)
    return `${localizeToken(match[1])} برای دوره منتهی به ${localizePeriod(match[2])} در دسترس است.`;
  match = summary.match(
    /^(.+) announcement for period ending (.+) matched an active Codal alert subscription\.$/,
  );
  if (match)
    return `اطلاعیه ${localizeToken(match[1])} برای دوره منتهی به ${localizePeriod(match[2])} با اشتراک هشدار فعال کدال تطبیق داده شد.`;
  match = summary.match(
    /^Monthly activity announcement for period ending (.+) matched an active Codal alert subscription\.$/,
  );
  if (match)
    return `اطلاعیه فعالیت ماهانه برای دوره منتهی به ${localizePeriod(match[1])} با اشتراک هشدار فعال کدال تطبیق داده شد.`;
  match = summary.match(/^Current volume is ([\d.]+) times its historical median\.$/);
  if (match) return `حجم فعلی ${toPersianDigits(match[1])} برابر میانه تاریخی آن است.`;
  match = summary.match(/^Current trading value is ([\d.]+) times its historical median\.$/);
  if (match) return `ارزش معاملات فعلی ${toPersianDigits(match[1])} برابر میانه تاریخی آن است.`;
  match = summary.match(/^A (.+)-side trade with value ([\d.]+) met the governed threshold\.$/);
  if (match)
    return `معامله در سمت ${localizeToken(match[1])} با ارزش ${toPersianDigits(match[2])} از آستانه تعیین‌شده عبور کرد.`;
  match = summary.match(/^Real-person average buy-to-sell volume ratio is ([\d.]+)\.$/);
  if (match) return `نسبت میانگین حجم خرید حقیقی به فروش حقیقی ${toPersianDigits(match[1])} است.`;
  match = summary.match(/^Real-person net traded value is ([+-]?[\d.]+)\.$/);
  if (match) return `خالص ارزش معاملات حقیقی ${toPersianDigits(match[1])} است.`;
  match = summary.match(/^The (buy|sell) queue value is ([\d.]+) after (\d+) seconds\.$/);
  if (match)
    return `ارزش صف ${localizeToken(match[1])} پس از ${toPersianDigits(match[3])} ثانیه ${toPersianDigits(match[2])} است.`;
  if (/^The current canonical volume crossed /.test(summary))
    return `حجم فعلی معاملات از مرز ناهنجاری تعیین‌شده عبور کرده است.`;
  if (/^The current canonical traded value crossed /.test(summary))
    return `ارزش فعلی معاملات از مرز ناهنجاری تعیین‌شده عبور کرده است.`;
  if (/^The ratio crossed /.test(summary))
    return `نسبت قدرت خریدار و فروشنده از مرز تعیین‌شده عبور کرده است.`;
  if (/^Canonical real-person buy and sell values crossed /.test(summary))
    return `ارزش خرید و فروش حقیقی از مرز جریان خالص تعیین‌شده عبور کرده‌اند.`;
  if (/^Allowed-price queue evidence crossed /.test(summary))
    return `شواهد صف در محدوده مجاز قیمت از مرز تعیین‌شده عبور کرده است.`;
  return hasLatinText(summary) ? fallbackSummary(insightType, symbol) : summary;
}

export function localizeEmptyStateMessage(value: string) {
  if (/^No current insights were found/i.test(value)) {
    return "در حال حاضر رویداد جدیدی برای نمادهای دیده‌بان شما یافت نشد.";
  }
  return hasLatinText(value) ? "در حال حاضر رویداد جدیدی یافت نشد." : value;
}
