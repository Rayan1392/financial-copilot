export type QuarterlyChangeTone = "positive" | "negative" | "neutral";

export interface QuarterlyChangeColors {
  tone: QuarterlyChangeTone;
  className: string;
  backgroundColor: string;
  textColor: string;
}

const EXPENSE_SOURCE_ITEM_IDS = new Set([1, 5, 12, 334]);

const TONES: Record<QuarterlyChangeTone, Omit<QuarterlyChangeColors, "tone">> = {
  positive: {
    className: "bg-emerald-100 text-emerald-900",
    backgroundColor: "#d1fae5",
    textColor: "#064e3b",
  },
  negative: {
    className: "bg-rose-100 text-rose-900",
    backgroundColor: "#ffe4e6",
    textColor: "#881337",
  },
  neutral: {
    className: "bg-white text-slate-700",
    backgroundColor: "#ffffff",
    textColor: "#1f2937",
  },
};

export function isQuarterlyExpenseRow(sourceItemId: number | null | undefined): boolean {
  return sourceItemId != null && EXPENSE_SOURCE_ITEM_IDS.has(sourceItemId);
}

/** Resolve the semantic colors for a QoQ percentage without changing its displayed value. */
export function resolveQuarterlyChangeColors(
  sourceItemId: number | null | undefined,
  changePercent: number | null | undefined,
): QuarterlyChangeColors {
  if (changePercent == null || !Number.isFinite(changePercent) || changePercent === 0) {
    return { tone: "neutral", ...TONES.neutral };
  }

  const isFavorable = isQuarterlyExpenseRow(sourceItemId)
    ? changePercent < 0
    : changePercent > 0;
  const tone = isFavorable ? "positive" : "negative";
  return { tone, ...TONES[tone] };
}
