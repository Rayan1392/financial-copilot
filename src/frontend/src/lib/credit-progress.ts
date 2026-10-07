export type CreditTone = "healthy" | "warning" | "danger";

export interface CreditProgress {
  /** Remaining credit clamped to be non-negative, for display. */
  remaining: number;
  /** Plan allowance, or null when the plan limit is unknown. */
  limit: number | null;
  /** 0-100 relative to the plan allowance; null when the limit is unknown. */
  percentage: number | null;
  tone: CreditTone | null;
}

export const WARNING_THRESHOLD_PERCENT = 50;
export const DANGER_THRESHOLD_PERCENT = 20;

export function toneForPercentage(percentage: number): CreditTone {
  if (percentage > WARNING_THRESHOLD_PERCENT) return "healthy";
  if (percentage >= DANGER_THRESHOLD_PERCENT) return "warning";
  return "danger";
}

export function computeCreditProgress(
  remainingCredit: number,
  planIncludedCredits: number | null | undefined,
): CreditProgress {
  const remaining = Number.isFinite(remainingCredit) ? Math.max(remainingCredit, 0) : 0;
  const limit =
    typeof planIncludedCredits === "number" &&
    Number.isFinite(planIncludedCredits) &&
    planIncludedCredits > 0
      ? planIncludedCredits
      : null;
  if (limit === null) return { remaining, limit, percentage: null, tone: null };

  const percentage = Math.min(Math.max((remaining / limit) * 100, 0), 100);
  return { remaining, limit, percentage, tone: toneForPercentage(percentage) };
}
