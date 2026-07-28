const tehran = "Asia/Tehran";

const jalaliDate = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
  timeZone: tehran,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

const jalaliDateTime = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
  timeZone: tehran,
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});

/** Provider publication values are date-only; never infer a time or substitute receipt time. */
export function formatDisclosurePublicationDate(value?: string) {
  if (!value) return "نامشخص";
  const [year, month, day] = value.slice(0, 10).split("-").map(Number);
  return jalaliDate.format(new Date(Date.UTC(year, month - 1, day, 12)));
}

/** System receipt is an instant and is displayed in Tehran time. */
export function formatDisclosureReceiptDate(value: string) {
  return jalaliDateTime.format(new Date(value));
}

const jalaliMonth = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
  timeZone: tehran,
  year: "numeric",
  month: "long",
});

const jalaliMonthNumber = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
  timeZone: tehran,
  month: "numeric",
});

type PeriodDisclosure = {
  type: string;
  reportingPeriodEnd?: string;
  reportingPeriodType?: string;
};

export function formatDisclosurePeriod(item: PeriodDisclosure) {
  const date = dateOnlyAtTehranNoon(item.reportingPeriodEnd);
  if (!date) return "نامشخص";
  return item.type === "MonthlyProductionSales"
    ? jalaliMonth.format(date)
    : `دوره منتهی به ${jalaliDate.format(date)}`;
}

export function formatDisclosurePeriodType(item: PeriodDisclosure) {
  if (item.type === "MonthlyProductionSales") {
    const date = dateOnlyAtTehranNoon(item.reportingPeriodEnd);
    return date ? jalaliMonthNumber.format(date) : "نامشخص";
  }

  const periodLabels: Record<string, string> = {
    ThreeMonths: "۳ ماهه",
    SixMonths: "۶ ماهه",
    NineMonths: "۹ ماهه",
    TwelveMonths: "۱۲ ماهه",
    Quarterly: "۳ ماهه",
    SemiAnnual: "۶ ماهه",
    Annual: "۱۲ ماهه",
  };
  return item.reportingPeriodType ? (periodLabels[item.reportingPeriodType] ?? item.reportingPeriodType) : "نامشخص";
}

function dateOnlyAtTehranNoon(value?: string) {
  if (!value) return undefined;
  const [year, month, day] = value.slice(0, 10).split("-").map(Number);
  if (!year || !month || !day) return undefined;
  return new Date(Date.UTC(year, month - 1, day, 12));
}

export const disclosureTypeLabels: Record<string, string> = {
  MonthlyProductionSales: "تولید و فروش ماهانه",
  IncomeStatement: "صورت سود و زیان",
  BalanceSheet: "ترازنامه",
  CashFlowStatement: "جریان وجه نقد",
};

export const consolidationLabel = (isComposing: boolean) => isComposing ? "تلفیقی" : "غیرتلفیقی";
