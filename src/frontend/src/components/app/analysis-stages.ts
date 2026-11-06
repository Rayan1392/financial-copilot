/**
 * The stages every answer goes through, in order. The API reports no progress while it works, so the indicator
 * advances on typical timings and then waits on the last stage until the answer arrives; it never claims completion.
 */
export const ANALYSIS_STAGES = [
  { label: "درک پرسش", startsAtMs: 0 },
  { label: "دریافت داده‌ها", startsAtMs: 2_500 },
  { label: "محاسبه و نگارش پاسخ", startsAtMs: 6_000 },
] as const;

export function analysisStageAt(elapsedMs: number): number {
  let stage = 0;
  ANALYSIS_STAGES.forEach((item, index) => {
    if (elapsedMs >= item.startsAtMs) stage = index;
  });
  return stage;
}
