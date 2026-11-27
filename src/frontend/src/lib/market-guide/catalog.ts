export type GuideAction = "chat" | "watchlist" | "events" | "telegram" | "support";

export interface GuideImage {
  alt: string;
  kind: "answer" | "screen";
  width: number;
  height: number;
  previewWidth: number;
  previewHeight: number;
}

export interface GuideExample {
  id: string;
  title: string;
  description: string;
  prompt: string;
  action: GuideAction;
  image?: GuideImage;
}

interface GuideGroup {
  id: string;
  title: string;
  examples: readonly GuideExample[];
}

export const MARKET_GUIDE_TITLE = "راهنمای تحلیل و رصد بازار";

export const marketGuideGroups: readonly GuideGroup[] = [
  {
    id: "sales",
    title: "تولید و فروش",
    examples: [
      {
        id: "monthly",
        title: "روند فروش ماهانه",
        description: "فروش ماه‌به‌ماه شرکت را با ماه مشابه سال قبل و میانگین ۱۲ماهه مقایسه کنید.",
        prompt: "روند فروش ماهانهٔ خزامیا را نشان بده.",
        action: "chat",
        image: {
          alt: "نمودار ماه‌به‌ماه فروش زامیاد با مقایسهٔ دو سال و میانگین ۱۲ماهه",
          kind: "answer",
          width: 3600,
          height: 2356,
          previewWidth: 960,
          previewHeight: 628,
        },
      },
      {
        id: "mix",
        title: "ترکیب فروش محصولات",
        description: "مبلغ فروش و سهم هر محصول از فروش شرکت را در یک دوره ببینید.",
        prompt: "ترکیب فروش محصولات کگهر را بررسی کن.",
        action: "chat",
        image: {
          alt: "جدول ترکیب درآمد محصولات کگهر، همراه با مبلغ و سهم فروش هر محصول",
          kind: "answer",
          width: 1060,
          height: 920,
          previewWidth: 960,
          previewHeight: 833,
        },
      },
      {
        id: "product",
        title: "روند فروش یک محصول",
        description: "مبلغ و نرخ فروش را کنار مقدار تولید و فروش یک محصول در طول زمان دنبال کنید.",
        prompt: "روند فروش گندلهٔ کگهر در ۱۲ ماه اخیر چطور بوده؟",
        action: "chat",
        image: {
          alt: "نمودار مبلغ، نرخ و مقدار فروش و تولید گندلهٔ کگهر",
          kind: "answer",
          width: 3552,
          height: 2841,
          previewWidth: 960,
          previewHeight: 768,
        },
      },
      {
        id: "ranking",
        title: "رتبه‌بندی گزارش‌های ماهانه",
        description:
          "گزارش‌های ماهانهٔ شرکت‌ها را بر اساس امتیاز کیفیت داده‌های تولید و فروش مرور کنید.",
        prompt: "رتبه‌بندی گزارش‌های ماهانه را نشان بده.",
        action: "chat",
        image: {
          alt: "جدول رتبه‌بندی گزارش‌های ماهانه بر اساس کیفیت داده‌های تولید و فروش",
          kind: "answer",
          width: 1259,
          height: 1159,
          previewWidth: 960,
          previewHeight: 884,
        },
      },
    ],
  },
  {
    id: "value",
    title: "ارزش‌گذاری",
    examples: [
      {
        id: "gauges",
        title: "گیج‌های ارزش‌گذاری",
        description:
          "جایگاه P/E و P/S سهم را در سابقهٔ خودش و جایگاه قیمت بازار را در بازهٔ قیمت تعادلی ببینید.",
        prompt: "گیج‌های ارزش‌گذاری گدنا را نشان بده.",
        action: "chat",
        image: {
          alt: "گیج‌های P/E و P/S گدنا و جایگاه آخرین قیمت در بازهٔ قیمت تعادلی",
          kind: "answer",
          width: 1599,
          height: 2313,
          previewWidth: 760,
          previewHeight: 1100,
        },
      },
      {
        id: "industry",
        title: "مقایسهٔ سهم با صنعت",
        description:
          "P/E، P/S و نسبت قیمت بازار به قیمت تعادلی را در کنار مقادیر هم‌گروهی‌ها و میانگین گروه بررسی کنید.",
        prompt: "شاراک را با صنعت خودش مقایسه کن.",
        action: "chat",
        image: {
          alt: "جدول مقایسهٔ شاراک و نمادهای هم‌صنعت، با میانگین گروه",
          kind: "answer",
          width: 918,
          height: 1202,
          previewWidth: 840,
          previewHeight: 1100,
        },
      },
      {
        id: "snapshot",
        title: "خلاصه با ارسال نام نماد",
        description:
          "با فرستادن نام نماد، یک نمای کوتاه از داده‌های موجود و مسیر دسترسی به گزارش‌های کامل بگیرید.",
        prompt: "دجابر",
        action: "chat",
        image: {
          alt: "کارت خلاصهٔ دجابر شامل فروش ماهانه و داده‌های فصلی",
          kind: "answer",
          width: 1272,
          height: 1051,
          previewWidth: 960,
          previewHeight: 793,
        },
      },
    ],
  },
  {
    id: "financials",
    title: "صورت‌های مالی",
    examples: [
      {
        id: "quarter",
        title: "عملکرد مالی فصلی",
        description:
          "فروش، سود ناخالص، حاشیهٔ سود، سود خالص و سود هر سهم را در فصل‌های مختلف مقایسه کنید.",
        prompt: "عملکرد مالی فصلی فملی را بررسی کن.",
        action: "chat",
        image: {
          alt: "جدول عملکرد فصلی فملی به همراه نمودار فروش و سود",
          kind: "answer",
          width: 2400,
          height: 3296,
          previewWidth: 801,
          previewHeight: 1100,
        },
      },
      {
        id: "balance",
        title: "صورت وضعیت مالی (ترازنامه)",
        description:
          "ترکیب دارایی‌ها، بدهی‌ها و حقوق صاحبان سهام را در آخرین گزارش و دوره‌های قبل بررسی کنید.",
        prompt: "صورت وضعیت مالی فملی را بررسی کن.",
        action: "chat",
        image: {
          alt: "ترازنامهٔ فملی و نمودار ساختار تأمین مالی دارایی‌ها",
          kind: "answer",
          width: 2400,
          height: 3240,
          previewWidth: 815,
          previewHeight: 1100,
        },
      },
      {
        id: "cash",
        title: "صورت جریان وجوه نقد",
        description:
          "جریان‌های نقد عملیاتی، سرمایه‌گذاری و تأمین مالی و ماندهٔ نقد پایان دوره را کنار هم ببینید.",
        prompt: "جریان وجه نقد فملی را نشان بده.",
        action: "chat",
        image: {
          alt: "صورت جریان وجوه نقد فملی با نمودار و جدول جریان‌های نقد",
          kind: "answer",
          width: 1046,
          height: 2548,
          previewWidth: 452,
          previewHeight: 1100,
        },
      },
    ],
  },
  {
    id: "deep",
    title: "تحلیل عمیق",
    examples: [
      {
        id: "deep",
        title: "پاسخ عمیق و جست‌وجوی وب",
        description:
          "پرسش خود را در حالت «عمیق» بفرستید و «جست‌وجوی وب» را فعال کنید تا ساپیو بتواند منابع وب مرتبط را هم بررسی کند.",
        prompt:
          "با پاسخ عمیق و جست‌وجوی وب بررسی کن چه عواملی می‌تواند رشد فروش مهر خزامیا را توضیح دهد.",
        action: "chat",
        image: {
          alt: "تنظیم حالت عمیق و جست‌وجوی وب در کادر گفت‌وگو، زیر پاسخ فروش زامیاد",
          kind: "screen",
          width: 1642,
          height: 1107,
          previewWidth: 960,
          previewHeight: 647,
        },
      },
    ],
  },
  {
    id: "follow",
    title: "دیده‌بان و دسترسی",
    examples: [
      {
        id: "watchlist",
        title: "دیده‌بان شخصی",
        description:
          "نمادهای مهم را به دیده‌بان اضافه کنید تا آخرین قیمت، تغییر روزانه و رویدادهای آن‌ها را در یک جا دنبال کنید.",
        prompt: "چطور خزامیا را به دیده‌بانم اضافه کنم؟",
        action: "watchlist",
        image: {
          alt: "صفحهٔ مدیریت دیده‌بان و فهرست نمادهای دنبال‌شده",
          kind: "screen",
          width: 1406,
          height: 1266,
          previewWidth: 960,
          previewHeight: 864,
        },
      },
      {
        id: "events",
        title: "خوراک هوشمند رویدادهای دیده‌بان",
        description:
          "رویدادهای نمادهای دنبال‌شده، از جمله ناهنجاری حجم معاملات و انتشار گزارش ماهانه یا صورت مالی را در زبانهٔ «رویدادها» ببینید. خوراک را می‌توان بر اساس نوع و سطح رویداد فیلتر کرد.",
        prompt: "در دیده‌بان، چه رویدادهایی برای نمادهای من ثبت شده است؟",
        action: "events",
        image: {
          alt: "خوراک رویدادهای دیده‌بان با نمونهٔ ناهنجاری فروش و اطلاعیهٔ کدال",
          kind: "screen",
          width: 1361,
          height: 1362,
          previewWidth: 960,
          previewHeight: 961,
        },
      },
      {
        id: "telegram",
        title: "پرسش و هشدار در تلگرام",
        description:
          "پرسش‌های بورسی را در ربات ساپیو بفرستید و برای نمادهای انتخابی، هشدار انتشار گزارش ماهانه و صورت مالی کدال را فعال کنید.",
        prompt: "روند فروش ماهانهٔ خزامیا را نشان بده.",
        action: "telegram",
      },
      {
        id: "support",
        title: "درخواست پشتیبانی",
        description:
          "از بخش «پیام‌ها» و گزینهٔ «درخواست جدید»، موضوع و شرح درخواست را بفرستید و پاسخ پشتیبانی را در همان گفت‌وگو پیگیری کنید.",
        prompt: "چرا آخرین قیمت دجابر در خلاصهٔ نماد نمایش داده نمی‌شود؟",
        action: "support",
        image: {
          alt: "بخش پیام‌های ساپیو با گزینهٔ درخواست جدید و نمونهٔ وضعیت پاسخ داده شد",
          kind: "screen",
          width: 1211,
          height: 333,
          previewWidth: 960,
          previewHeight: 264,
        },
      },
    ],
  },
];
