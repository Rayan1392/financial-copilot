import { createRoot } from "react-dom/client";
import type { SymbolSnapshotResult } from "@/lib/chat.functions";
import { SymbolSnapshotCard } from "../symbol-snapshot-card";
import "../../../styles.css";

// Feature 156 browser fixture: ?fixture=full|partial|bank. The numbers are illustrative.
const full: SymbolSnapshotResult = {
  symbol: "اخابر",
  companyName: "مخابرات ایران",
  marketName: "بورس اوراق بهادار تهران",
  groupName: "مخابرات",
  introFa: "خلاصهٔ آخرین داده‌های «اخابر». برای جزئیات یکی از گزینه‌های زیر را انتخاب کنید.",
  price: {
    status: "Available",
    latestPrice: 3450,
    dailyChangePercent: -2.1,
    tradingDateJalali: "1405/08/18",
  },
  monthlySales: {
    status: "Available",
    periodLabelFa: "مهر 1405",
    unitLabelFa: "میلیون ریال",
    salesAmount: 123456789,
    monthOverMonthPercent: 4.25,
    yearOverYearPercent: 31.5,
  },
  quarterlyReport: {
    status: "Available",
    periodLabelFa: "فصل دوم 1405",
    isAudited: false,
    rows: [
      { labelFa: "فروش", unitLabelFa: "میلیون ریال", value: 987654321, yearOverYearPercent: 120.4 },
      {
        labelFa: "سود (زیان) ناخالص",
        unitLabelFa: "میلیون ریال",
        value: 123456789,
        yearOverYearPercent: -12.75,
      },
      {
        labelFa: "سود (زیان) خالص",
        unitLabelFa: "میلیون ریال",
        value: -98765432,
        yearOverYearPercent: null,
      },
      {
        labelFa: "سود هر سهم پس از کسر مالیات",
        unitLabelFa: "ریال",
        value: 1234,
        yearOverYearPercent: 8,
      },
    ],
  },
};

const fixtures: Record<string, SymbolSnapshotResult> = {
  full,
  partial: {
    ...full,
    monthlySales: { status: "NoData", noticeFa: "گزارش ماهانه‌ای برای این نماد ثبت نشده است." },
    price: { status: "Unavailable", noticeFa: "این بخش فعلاً در دسترس نیست." },
  },
  bank: {
    ...full,
    symbol: "وبملت",
    companyName: "بانک ملت",
    quarterlyReport: {
      ...full.quarterlyReport,
      rows: [
        {
          labelFa: "خالص درآمد تسهیلات و سپرده‌گذاری",
          unitLabelFa: "میلیون ریال",
          value: 123456789012,
          yearOverYearPercent: 45.2,
        },
        {
          labelFa: "خالص درآمد کارمزد",
          unitLabelFa: "میلیون ریال",
          value: 9876543210,
          yearOverYearPercent: -3.4,
        },
      ],
    },
  },
};

const data = fixtures[new URLSearchParams(location.search).get("fixture") ?? "full"];

createRoot(document.getElementById("root")!).render(
  <main className="mx-auto max-w-3xl p-2 sm:p-4">
    <SymbolSnapshotCard data={data} />
  </main>,
);
