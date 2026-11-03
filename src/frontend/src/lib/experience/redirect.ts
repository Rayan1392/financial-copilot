import { toMobileAutoPath } from "@/lib/experience/path-map";
import {
  resolveExperienceWithReason,
  type ExperienceReason,
  type ExperienceSignals,
} from "@/lib/experience/resolve-experience";

export interface AutomaticRedirect {
  target: string;
  reason: ExperienceReason;
}

/**
 * The automatic desktop → mobile decision, shared by the server adapter and the client guard. It is
 * one-directional, so `automaticMobileRedirect(target)` is always null and redirects never cycle (spec §7.4).
 */
export function automaticMobileRedirect(
  href: string,
  signals: ExperienceSignals,
  { enabled }: { enabled: boolean },
): AutomaticRedirect | null {
  if (!enabled) return null;
  const target = toMobileAutoPath(href);
  if (target === null) return null;
  const { experience, reason } = resolveExperienceWithReason(signals);
  return experience === "mobile" ? { target, reason } : null;
}
