// Pure helpers for the TahlilApp launch pages (Feature 139, Design §8, §14).

// Opaque, URL-safe base64 code. Mirrors the backend shape check so malformed codes never leave the browser.
const LAUNCH_CODE_PATTERN = /^[A-Za-z0-9_-]{16,256}$/;

const LAUNCH_ERROR_PREFIX = "https://financialcopilot/errors/";

export const LAUNCH_FAILURE_REASONS = ["invalid", "unavailable", "disabled", "unknown"] as const;

export type LaunchFailureReason = (typeof LAUNCH_FAILURE_REASONS)[number];

/** Returns the launch code from a query string, or null when it is absent or malformed. */
export function extractLaunchCode(search: string): string | null {
  const code = new URLSearchParams(search).get("code");
  return code !== null && LAUNCH_CODE_PATTERN.test(code) ? code : null;
}

/** Maps a ProblemDetails type to the closed set of failure reasons shown on /launch-failed. */
export function launchFailureReasonFor(problemType: string | undefined): LaunchFailureReason {
  switch (problemType) {
    case `${LAUNCH_ERROR_PREFIX}launch-invalid`:
      return "invalid";
    case `${LAUNCH_ERROR_PREFIX}launch-unavailable`:
      return "unavailable";
    case `${LAUNCH_ERROR_PREFIX}launch-account-disabled`:
      return "disabled";
    default:
      return "unknown";
  }
}

/**
 * Validates the optional TahlilApp return URL shown on /launch-failed (Design §14).
 * Returns the configured value when it is an absolute https URL without credentials, or an
 * http localhost URL while developing. Anything else, including an empty value, returns null.
 */
export function validateTahlilAppReturnUrl(
  value: string | undefined,
  options: { isDev: boolean },
): string | null {
  const candidate = value?.trim() ?? "";
  if (candidate === "") return null;

  let parsed: URL;
  try {
    parsed = new URL(candidate);
  } catch {
    return null;
  }
  if (parsed.username !== "" || parsed.password !== "") return null;
  if (parsed.protocol === "https:") return candidate;
  if (parsed.protocol === "http:" && options.isDev && parsed.hostname === "localhost") return candidate;
  return null;
}

/** Accepts only the closed reason set; anything else (including reflected input) becomes "unknown". */
export function parseLaunchFailureReason(value: unknown): LaunchFailureReason {
  return typeof value === "string" && (LAUNCH_FAILURE_REASONS as readonly string[]).includes(value)
    ? (value as LaunchFailureReason)
    : "unknown";
}
