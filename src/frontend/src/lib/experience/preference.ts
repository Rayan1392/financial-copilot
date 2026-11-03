/**
 * The remembered experience choice (Feature 153 spec §7.5). The cookie holds no personal data and is readable by
 * script, because the client-side guard needs it. Clearing it restores automatic detection.
 */
export type Experience = "mobile" | "desktop";

export const EXPERIENCE_COOKIE = "sapio_experience";
export const EXPERIENCE_QUERY_PARAM = "experience";
const ONE_YEAR_SECONDS = 31_536_000;

export function parseExperienceValue(value: unknown): Experience | null {
  return value === "mobile" || value === "desktop" ? value : null;
}

/** Reads the preference from a `Cookie` header or `document.cookie`. Unknown values are ignored. */
export function parseExperiencePreference(
  cookieHeader: string | null | undefined,
): Experience | null {
  if (!cookieHeader) return null;
  for (const part of cookieHeader.split(";")) {
    const [name, ...rest] = part.trim().split("=");
    if (name === EXPERIENCE_COOKIE) return parseExperienceValue(rest.join("=").trim());
  }
  return null;
}

export function serializeExperienceCookie(
  value: Experience,
  { secure }: { secure: boolean },
): string {
  const attributes = [
    `${EXPERIENCE_COOKIE}=${value}`,
    "Path=/",
    `Max-Age=${ONE_YEAR_SECONDS}`,
    "SameSite=Lax",
  ];
  if (secure) attributes.push("Secure");
  return attributes.join("; ");
}

/** The one-shot `?experience=mobile|desktop` override. Any other value is ignored. */
export function parseExperienceOverride(search: string): Experience | null {
  return parseExperienceValue(new URLSearchParams(search).get(EXPERIENCE_QUERY_PARAM));
}

/** The search string with the override parameter removed (`""` when nothing else remains). */
export function withoutExperienceOverride(search: string): string {
  const params = new URLSearchParams(search);
  params.delete(EXPERIENCE_QUERY_PARAM);
  const rest = params.toString();
  return rest ? `?${rest}` : "";
}

/** `Secure` is omitted only for plain-HTTP loopback development. */
export function isSecureContextHost(protocol: string, hostname: string): boolean {
  if (protocol === "https:") return true;
  return !(hostname === "localhost" || hostname === "127.0.0.1" || hostname === "[::1]");
}
