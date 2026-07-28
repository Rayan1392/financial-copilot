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

/** System receipt is an instant and is displayed in Tehran time with an explicit zone label. */
export function formatDisclosureReceiptDate(value: string) {
  return `${jalaliDateTime.format(new Date(value))} (تهران)`;
}

export const disclosureTypeLabels: Record<string, string> = {
  MonthlyProductionSales: "تولید و فروش ماهانه",
  IncomeStatement: "صورت سود و زیان",
  BalanceSheet: "ترازنامه",
  CashFlowStatement: "جریان وجه نقد",
};

export const consolidationLabel = (isComposing: boolean) => isComposing ? "تلفیقی" : "غیرتلفیقی";
