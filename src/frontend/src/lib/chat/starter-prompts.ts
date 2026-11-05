import {
  BarChart3,
  FileText,
  ListOrdered,
  PieChart,
  Scale,
  TrendingUp,
  type LucideIcon,
} from "lucide-react";

/** Visual family of a starter card; each tone maps to theme-aware design tokens. */
export type StarterPromptTone = "sales" | "statement";

export interface StarterPrompt {
  /** The exact question sent to the assistant. */
  text: string;
  icon: LucideIcon;
  /** Short topic label shown above the question on the desktop cards. */
  topic: string;
  tone: StarterPromptTone;
}

/** The one list of new-chat starter prompts, shared by desktop `/chat` and mobile `/m`. */
export const STARTER_PROMPTS: readonly StarterPrompt[] = [
  {
    text: "ترکیب فروش محصولات کگهر را بررسی کن",
    icon: PieChart,
    topic: "ترکیب فروش",
    tone: "sales",
  },
  {
    text: "روند فروش ماهانه کگهر را نشان بده",
    icon: TrendingUp,
    topic: "فروش ماهانه",
    tone: "sales",
  },
  {
    text: "روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟",
    icon: BarChart3,
    topic: "عملکرد محصول",
    tone: "sales",
  },
  {
    text: "رتبه‌بندی گزارش‌های ماهانه را نشان بده",
    icon: ListOrdered,
    topic: "رتبه‌بندی",
    tone: "sales",
  },
  {
    text: "عملکرد مالی فصلی فولاد را بررسی کن",
    icon: FileText,
    topic: "سود و زیان فصلی",
    tone: "statement",
  },
  { text: "صورت وضعیت مالی فاذر را بررسی کن", icon: Scale, topic: "ترازنامه", tone: "statement" },
];
