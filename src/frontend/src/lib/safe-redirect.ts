// Open-redirect guard for redirect-like search parameters (F-11).
// Only a same-origin absolute path survives; anything else falls back to the given path.

const DEFAULT_FALLBACK = "/chat";
const INTERNAL_BASE = "https://internal.invalid";

// ASCII control characters, space, and any other whitespace.
const CONTROL_OR_WHITESPACE = /[\x00-\x20\x7f\s]/;

function isInternalPath(candidate: string): boolean {
  if (!candidate.startsWith("/")) return false;
  // "//host" and "/\host" are protocol-relative for browsers; "\" is treated as "/" by URL parsing.
  const second = candidate.charAt(1);
  if (second === "/" || second === "\\") return false;
  if (candidate.includes("\\")) return false;
  if (CONTROL_OR_WHITESPACE.test(candidate)) return false;
  try {
    return new URL(candidate, INTERNAL_BASE).origin === INTERNAL_BASE;
  } catch {
    return false;
  }
}

/**
 * Returns the value unchanged when it is an internal absolute path, otherwise the fallback.
 * The percent-decoded form is checked too, so encoded slashes or backslashes cannot slip through.
 */
export function safeInternalPath(value: unknown, fallback = DEFAULT_FALLBACK): string {
  if (typeof value !== "string" || !isInternalPath(value)) return fallback;
  let decoded: string;
  try {
    decoded = decodeURIComponent(value);
  } catch {
    return fallback;
  }
  return isInternalPath(decoded) ? value : fallback;
}
