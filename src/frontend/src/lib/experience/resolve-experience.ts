import type { Experience } from "@/lib/experience/preference";

/**
 * Decides which experience a device gets (Feature 153 spec §7.1). Pure, and shared by the server redirect and
 * the client guard so detection logic exists once.
 *   1. An explicit preference always wins.
 *   2. A mobile client hint (`Sec-CH-UA-Mobile: ?1`, `navigator.userAgentData.mobile`) means mobile.
 *   3. A phone user agent means mobile.
 *   4. Everything else is desktop: desktops, iPads (iPadOS reports desktop Safari), Android tablets, bots, unknown.
 */

const PHONE_USER_AGENT = /iPhone|iPod|Android.*Mobile|Mobile.*Firefox|Opera Mini|Windows Phone/i;
const BOT_USER_AGENT = /bot|crawler|spider|crawling|slurp|bingpreview/i;

export function isPhoneUserAgent(userAgent: string | null | undefined): boolean {
  if (!userAgent) return false;
  if (BOT_USER_AGENT.test(userAgent)) return false;
  return PHONE_USER_AGENT.test(userAgent);
}

/** Parses `Sec-CH-UA-Mobile` (`?1` / `?0`). Absent or malformed headers give null. */
export function parseMobileClientHint(header: string | null | undefined): boolean | null {
  if (header === "?1") return true;
  if (header === "?0") return false;
  return null;
}

export type ExperienceReason = "preference" | "ch" | "ua" | "default";

export interface ExperienceSignals {
  preference: Experience | null;
  uaMobileHint: boolean | null;
  userAgent: string | null | undefined;
}

export function resolveExperienceWithReason(signals: ExperienceSignals): {
  experience: Experience;
  reason: ExperienceReason;
} {
  if (signals.preference) return { experience: signals.preference, reason: "preference" };
  if (signals.uaMobileHint === true && !BOT_USER_AGENT.test(signals.userAgent ?? "")) {
    return { experience: "mobile", reason: "ch" };
  }
  if (isPhoneUserAgent(signals.userAgent)) return { experience: "mobile", reason: "ua" };
  return { experience: "desktop", reason: "default" };
}

export function resolveExperience(signals: ExperienceSignals): Experience {
  return resolveExperienceWithReason(signals).experience;
}
