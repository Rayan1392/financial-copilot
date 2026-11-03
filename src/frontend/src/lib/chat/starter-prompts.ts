import { BarChart3, FileText, PieChart, TrendingUp, type LucideIcon } from "lucide-react";

export interface StarterPrompt {
  text: string;
  icon: LucideIcon;
}

/** The one list of new-chat starter prompts, shared by desktop `/chat` and mobile `/m`. */
export const STARTER_PROMPTS: readonly StarterPrompt[] = [
  { text: "ترکیب فروش محصولات کگهر را بررسی کن", icon: PieChart },
  { text: "روند فروش ماهانه کگهر را نشان بده", icon: TrendingUp },
  { text: "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟", icon: BarChart3 },
  { text: "رتبه‌بندی گزارش‌های ماهانه را نشان بده", icon: FileText },
  { text: "گزارش فصلی فملی", icon: FileText },
  { text: "گزارش سود و زیان فصلی فولاد را بررسی کن", icon: FileText },
];
