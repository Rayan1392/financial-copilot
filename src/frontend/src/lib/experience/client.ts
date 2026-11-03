import {
  isSecureContextHost,
  parseExperienceOverride,
  parseExperiencePreference,
  serializeExperienceCookie,
  withoutExperienceOverride,
  type Experience,
} from "@/lib/experience/preference";
import { automaticMobileRedirect } from "@/lib/experience/redirect";
import { MOBILE_AUTO_REDIRECT_ENABLED } from "@/lib/experience/flag";

/** Browser adapter for the experience decision (Feature 153 T06). Call only in the browser. */

type UserAgentDataNavigator = Navigator & { userAgentData?: { mobile?: boolean } };

export function readExperiencePreference(): Experience | null {
  return parseExperiencePreference(document.cookie);
}

/** Written before any manual-switch navigation, so the next resolution agrees with the destination. */
export function writeExperiencePreference(value: Experience) {
  document.cookie = serializeExperienceCookie(value, {
    secure: isSecureContextHost(window.location.protocol, window.location.hostname),
  });
}

/** The mapped `/m` path when this device should get mobile, otherwise null. Flag-gated (default off). */
export function clientMobileRedirectTarget(
  href: string,
  enabled = MOBILE_AUTO_REDIRECT_ENABLED,
): string | null {
  const nav = navigator as UserAgentDataNavigator;
  const decision = automaticMobileRedirect(
    href,
    {
      preference: readExperiencePreference(),
      uaMobileHint:
        typeof nav.userAgentData?.mobile === "boolean" ? nav.userAgentData.mobile : null,
      userAgent: nav.userAgent,
    },
    { enabled },
  );
  return decision?.target ?? null;
}

/**
 * Applies a one-shot `?experience=` override: stores it and removes the parameter from the address bar.
 * Runs regardless of the rollout flag, because it is an explicit user (or support) choice.
 */
export function applyExperienceOverride() {
  const override = parseExperienceOverride(window.location.search);
  if (!override) return;
  writeExperiencePreference(override);
  const { pathname, search, hash } = window.location;
  window.history.replaceState(
    window.history.state,
    "",
    pathname + withoutExperienceOverride(search) + hash,
  );
}
