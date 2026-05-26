// Mock AI response generator + canned market data for the Tehran Stock Exchange.
export type Valuation = "ارزنده" | "متعادل" | "گران";

export interface StockCard {
  symbol: string;
  fullName: string;
  price: number;
  changePercent: number;
  forwardPE: number;
  industryPE: number;
  revenueGrowth: number;
  netProfitGrowth: number;
  valuation: Valuation;
  confidence: number;
  sparkline: number[];
}

export interface TableBlock {
  title: string;
  columns: string[];
  rows: Array<Array<string>>;
  highlightCol?: number;
}

export interface ScreenerBlock {
  title: string;
  detectedFilters: { label: string; value: string }[];
  results: Array<{
    symbol: string;
    name: string;
    price: number;
    changePercent: number;
    pe: number;
    marketCap: string;
  }>;
}

export interface MarketSnapshot {
  totalIndex: number;
  totalIndexChange: number;
  weightedIndex: number;
  weightedIndexChange: number;
  realMoneyFlow: number;
  tradingVolume: number;
  topGainers: { symbol: string; change: number }[];
  topLosers: { symbol: string; change: number }[];
  trendingIndustries: { name: string; change: number }[];
  insight: string;
}

export interface ResearchStep {
  label: string;
  detail: string;
}

export interface ChatBlock {
  message: string;
  confidence: number;
  cards?: StockCard[];
  tables?: TableBlock[];
  screener?: ScreenerBlock;
  research?: ResearchStep[];
  portfolio?: PortfolioBlock;
  suggestedQuestions: string[];
  creditsUsed: number;
}

export interface PortfolioBlock {
  score: number;
  diversificationLabel: string;
  concentrationRisk: "کم" | "متوسط" | "زیاد";
  allocations: { sector: string; percent: number }[];
  holdings: { symbol: string; weight: number; pnl: number }[];
  recommendations: string[];
}

const SPARK_UP = [12, 14, 11, 16, 18, 17, 22, 25, 24, 27, 29, 32];
const SPARK_DOWN = [28, 26, 27, 22, 20, 21, 18, 16, 17, 14, 12, 10];
const SPARK_VOL = [18, 22, 14, 24, 19, 26, 17, 22, 21, 28, 24, 30];

export const STOCK_DB: Record<string, StockCard> = {
  توسن: {
    symbol: "توسن",
    fullName: "توسعه سامانه‌های نرم‌افزاری نگین",
    price: 23450,
    changePercent: 2.4,
    forwardPE: 5.8,
    industryPE: 9.4,
    revenueGrowth: 32,
    netProfitGrowth: 71,
    valuation: "ارزنده",
    confidence: 0.87,
    sparkline: SPARK_UP,
  },
  فملی: {
    symbol: "فملی",
    fullName: "ملی صنایع مس ایران",
    price: 8420,
    changePercent: 1.9,
    forwardPE: 7.2,
    industryPE: 8.8,
    revenueGrowth: 18,
    netProfitGrowth: 12,
    valuation: "متعادل",
    confidence: 0.74,
    sparkline: SPARK_VOL,
  },
  شستا: {
    symbol: "شستا",
    fullName: "سرمایه‌گذاری تأمین اجتماعی",
    price: 1124,
    changePercent: -0.8,
    forwardPE: 9.1,
    industryPE: 10.4,
    revenueGrowth: 9,
    netProfitGrowth: 4,
    valuation: "متعادل",
    confidence: 0.62,
    sparkline: SPARK_DOWN,
  },
  کگل: {
    symbol: "کگل",
    fullName: "معدنی و صنعتی گل‌گهر",
    price: 5430,
    changePercent: 4.8,
    forwardPE: 6.4,
    industryPE: 8.1,
    revenueGrowth: 24,
    netProfitGrowth: 33,
    valuation: "ارزنده",
    confidence: 0.81,
    sparkline: SPARK_UP,
  },
  اخابر: {
    symbol: "اخابر",
    fullName: "مخابرات ایران",
    price: 9210,
    changePercent: -1.2,
    forwardPE: 11.4,
    industryPE: 9.6,
    revenueGrowth: 6,
    netProfitGrowth: -3,
    valuation: "گران",
    confidence: 0.58,
    sparkline: SPARK_DOWN,
  },
  آسیاتک: {
    symbol: "آسیاتک",
    fullName: "انتقال داده‌های آسیاتک",
    price: 14300,
    changePercent: 1.4,
    forwardPE: 8.6,
    industryPE: 9.6,
    revenueGrowth: 22,
    netProfitGrowth: 18,
    valuation: "متعادل",
    confidence: 0.71,
    sparkline: SPARK_VOL,
  },
};

export const MARKET_SNAPSHOT: MarketSnapshot = {
  totalIndex: 2150432,
  totalIndexChange: -0.12,
  weightedIndex: 712840,
  weightedIndexChange: 0.34,
  realMoneyFlow: -480,
  tradingVolume: 4280,
  topGainers: [
    { symbol: "کگل", change: 4.8 },
    { symbol: "توسن", change: 2.4 },
    { symbol: "فملی", change: 1.9 },
  ],
  topLosers: [
    { symbol: "اخابر", change: -1.2 },
    { symbol: "شستا", change: -0.8 },
    { symbol: "خودرو", change: -2.4 },
  ],
  trendingIndustries: [
    { name: "فلزات اساسی", change: 2.1 },
    { name: "کامپیوتر و نرم‌افزار", change: 1.7 },
    { name: "بانک‌ها", change: 1.4 },
    { name: "خودرو و قطعات", change: -1.8 },
  ],
  insight:
    "تقاضا در گروه بانکی و فلزات اساسی به‌شدت افزایش یافته است. خروج پول از صندوق‌های با درآمد ثابت مشاهده می‌شود که سیگنال ورود به بازار سرمایه است.",
};

// --- Reply generators ---

function compareTable(a: StockCard, b: StockCard): TableBlock {
  return {
    title: `مقایسه ${a.symbol} و ${b.symbol}`,
    columns: ["شاخص", a.symbol, b.symbol],
    rows: [
      ["نسبت P/E", String(a.forwardPE), String(b.forwardPE)],
      ["رشد فروش (YoY)", `+${a.revenueGrowth}٪`, `+${b.revenueGrowth}٪`],
      ["رشد سود خالص", `${a.netProfitGrowth > 0 ? "+" : ""}${a.netProfitGrowth}٪`, `${b.netProfitGrowth > 0 ? "+" : ""}${b.netProfitGrowth}٪`],
      ["ارزش‌گذاری", a.valuation, b.valuation],
      ["درجه اطمینان", `${Math.round(a.confidence * 100)}٪`, `${Math.round(b.confidence * 100)}٪`],
    ],
    highlightCol: a.confidence >= b.confidence ? 1 : 2,
  };
}

function fundamentalTable(s: StockCard): TableBlock {
  return {
    title: "شاخص‌های بنیادی کلیدی",
    columns: ["شاخص عملکرد", "مقدار فعلی", "رشد (YoY)"],
    rows: [
      ["درآمد عملیاتی", "۱,۲۴۰ B", `+${s.revenueGrowth}٪`],
      ["سود خالص", "۳۱۰ B", `+${s.netProfitGrowth}٪`],
      ["حاشیه سود", "۲۵٪", "+۳٪"],
      ["ROE", "۳۲٪", "+۵٪"],
    ],
  };
}

function detectStocks(q: string): StockCard[] {
  return Object.keys(STOCK_DB)
    .filter((sym) => q.includes(sym))
    .map((sym) => STOCK_DB[sym]);
}

const SCREENER_SAMPLE: ScreenerBlock = {
  title: "نتایج فیلتر هوشمند",
  detectedFilters: [
    { label: "نسبت P/E", value: "زیر ۶" },
    { label: "رشد فروش", value: "بالای ۲۰٪" },
    { label: "ارزش بازار", value: "بالای ۱۰ همت" },
  ],
  results: [
    { symbol: "توسن", name: "توسعه سامانه‌های نگین", price: 23450, changePercent: 2.4, pe: 5.8, marketCap: "۴۲ همت" },
    { symbol: "کگل", name: "گل‌گهر", price: 5430, changePercent: 4.8, pe: 6.4, marketCap: "۲۱۰ همت" },
    { symbol: "شپنا", name: "پالایش نفت اصفهان", price: 4120, changePercent: 4.9, pe: 5.4, marketCap: "۸۸ همت" },
    { symbol: "وغدیر", name: "سرمایه‌گذاری غدیر", price: 9120, changePercent: 1.1, pe: 5.9, marketCap: "۱۵۲ همت" },
  ],
};

const PORTFOLIO_SAMPLE: PortfolioBlock = {
  score: 7.4,
  diversificationLabel: "تنوع متوسط",
  concentrationRisk: "متوسط",
  allocations: [
    { sector: "فلزات اساسی", percent: 42 },
    { sector: "نرم‌افزاری", percent: 24 },
    { sector: "هلدینگ سرمایه‌گذاری", percent: 20 },
    { sector: "معدنی", percent: 14 },
  ],
  holdings: [
    { symbol: "توسن", weight: 24, pnl: 18.2 },
    { symbol: "فملی", weight: 32, pnl: 6.4 },
    { symbol: "شستا", weight: 20, pnl: -3.1 },
    { symbol: "کگل", weight: 14, pnl: 11.6 },
  ],
  recommendations: [
    "وزن گروه فلزات اساسی بالاتر از حد بهینه است؛ کاهش ۸-۱۰ درصدی پیشنهاد می‌شود.",
    "افزودن یک نماد از گروه دارویی برای کاهش بتای پرتفو مناسب است.",
    "نسبت ریسک به ریوارد فعلی پرتفو ۱:۲.۳ ارزیابی می‌شود.",
  ],
};

export function generateMockReply(prompt: string, deepResearch: boolean): ChatBlock {
  const q = prompt.trim();
  const mentioned = detectStocks(q);
  const baseCredits = deepResearch ? 18 : 4;

  // Screener intent
  if (/فیلتر|سهم.*ها|اسکرینر|P\/E|نسبت|بالای|زیر/i.test(q) && !mentioned.length) {
    return {
      message:
        "بر اساس فیلترهای استخراج‌شده از پرسش شما، ۴ نماد شرایط را برآورده می‌کنند. مرتب‌سازی پیش‌فرض بر اساس کیفیت سیگنال است.",
      confidence: 0.78,
      screener: SCREENER_SAMPLE,
      suggestedQuestions: [
        "این فیلتر را روی صنعت بانک هم اعمال کن",
        "از این لیست کدام برای میان‌مدت بهتر است؟",
      ],
      creditsUsed: baseCredits + 2,
    };
  }

  // Portfolio intent
  if (/پرتفو|سبد|پرتفوی|سهام من|سبد من/i.test(q)) {
    return {
      message:
        "تحلیل پرتفوی شما تکمیل شد. وضعیت کلی متعادل با اندکی تمرکز در گروه فلزات اساسی است.",
      confidence: 0.83,
      portfolio: PORTFOLIO_SAMPLE,
      suggestedQuestions: [
        "ریسک نوسانی پرتفو چقدر است؟",
        "اگر فملی را بفروشم چه جایگزینی پیشنهاد می‌کنی؟",
      ],
      creditsUsed: baseCredits + 4,
    };
  }

  // Market summary
  if (/بازار|شاخص|امروز|خلاصه|اوضاع/i.test(q) && !mentioned.length) {
    return {
      message:
        "شاخص کل بورس امروز با افت ۰.۱۲٪ به ۲,۱۵۰,۴۳۲ واحد رسید؛ گروه فلزات اساسی پیشتاز بازار بود اما خروج پول حقیقی ۴۸۰ میلیارد تومان ثبت شد.",
      confidence: 0.91,
      tables: [
        {
          title: "وضعیت گروه‌های پیشرو",
          columns: ["صنعت", "تغییر", "ارزش معاملات"],
          rows: [
            ["فلزات اساسی", "+۲.۱٪", "۸۴۰ B"],
            ["کامپیوتر و نرم‌افزار", "+۱.۷٪", "۳۲۰ B"],
            ["بانک‌ها", "+۱.۴٪", "۵۶۰ B"],
            ["خودرو و قطعات", "-۱.۸٪", "۴۱۰ B"],
          ],
        },
      ],
      suggestedQuestions: [
        "نمادهای پیشتاز فلزات اساسی را نشان بده",
        "چرا خودرو منفی است؟",
      ],
      creditsUsed: baseCredits,
    };
  }

  // Comparison: two stocks mentioned
  if (mentioned.length >= 2) {
    const [a, b] = mentioned;
    return {
      message: `مقایسه ${a.symbol} و ${b.symbol} از منظر بنیادی نشان می‌دهد ${a.confidence >= b.confidence ? a.symbol : b.symbol} از کیفیت سیگنال بالاتری برخوردار است.`,
      confidence: 0.82,
      cards: [a, b],
      tables: [compareTable(a, b)],
      suggestedQuestions: [
        `ریسک‌های ${a.symbol} چیست؟`,
        `مقایسه نسبت‌های نقدینگی ${a.symbol} و ${b.symbol}`,
      ],
      creditsUsed: baseCredits + 3,
    };
  }

  // Single stock analysis
  if (mentioned.length === 1) {
    const s = mentioned[0];
    const research: ResearchStep[] | undefined = deepResearch
      ? [
          { label: "بررسی صورت‌های مالی ۴ فصل اخیر", detail: "بارگذاری گزارش‌های میان‌دوره‌ای و سالانه" },
          { label: "تحلیل گزارش‌های کدال", detail: "۱۲ افشای با اهمیت در ۹۰ روز اخیر" },
          { label: "مقایسه با همگروهان صنعت", detail: "۶ نماد همتراز بررسی شد" },
          { label: "تحلیل جریان نقدینگی حقیقی", detail: "ورود/خروج پول هوشمند بازسازی شد" },
        ]
      : undefined;

    return {
      message: `بررسی نماد ${s.symbol} (${s.fullName}) نشان‌دهنده وضعیت بنیادی ${s.valuation === "ارزنده" ? "مستحکم" : s.valuation === "متعادل" ? "متعادل" : "کششی"} با سیگنال‌های ${s.changePercent >= 0 ? "مثبت" : "خنثی"} در جریان نقدینگی است. در ادامه جزئیات تحلیل را مشاهده می‌کنید:`,
      confidence: s.confidence,
      cards: [s],
      tables: [fundamentalTable(s)],
      research,
      suggestedQuestions: [
        `ریسک‌های ${s.symbol} چیست؟`,
        `مقایسه ${s.symbol} با صنعت`,
        `پیش‌بینی سود سالانه ${s.symbol}`,
      ],
      creditsUsed: baseCredits + (deepResearch ? 8 : 2),
    };
  }

  // Default
  return {
    message:
      "می‌توانم در تحلیل بنیادی، فیلتر هوشمند، خلاصه بازار و بررسی پرتفو کمکتان کنم. یک نماد یا موضوع را مطرح کنید.",
    confidence: 0.6,
    suggestedQuestions: [
      "خلاصه بازار امروز را بگو",
      "توسن ارزنده است؟",
      "بین اخابر و آسیاتک کدام بهتر است؟",
      "سهم‌هایی با P/E زیر ۶ و رشد بالا",
    ],
    creditsUsed: 1,
  };
}

export function generateThreadTitle(prompt: string): string {
  const stocks = detectStocks(prompt);
  if (stocks.length === 1) return `تحلیل ${stocks[0].symbol}`;
  if (stocks.length >= 2) return `مقایسه ${stocks[0].symbol} و ${stocks[1].symbol}`;
  if (/بازار|شاخص/.test(prompt)) return "خلاصه بازار";
  if (/پرتفو|سبد/.test(prompt)) return "تحلیل پرتفو";
  if (/فیلتر/.test(prompt)) return "فیلتر هوشمند";
  return prompt.slice(0, 32);
}
