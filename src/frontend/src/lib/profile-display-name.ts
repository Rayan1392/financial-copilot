export const MISSING_PROFILE_VALUE = "—";

type ProfileNames = {
  displayName?: string | null;
  firstName?: string | null;
  lastName?: string | null;
};

/** The official TahlilApp full name, trimmed; null when it is missing, empty or whitespace-only. */
export function officialDisplayName(displayName: string | null | undefined): string | null {
  const trimmed = displayName?.trim();
  return trimmed ? trimmed : null;
}

/**
 * Name shown for a TahlilApp profile: the official full name, else the older first + last name, else «—».
 * Null or blank parts are skipped, so the result is never "null null".
 */
export function profileDisplayName({ displayName, firstName, lastName }: ProfileNames): string {
  const official = officialDisplayName(displayName);
  if (official) return official;

  const joined = [firstName, lastName]
    .map((part) => part?.trim())
    .filter((part): part is string => Boolean(part))
    .join(" ");
  return joined || MISSING_PROFILE_VALUE;
}
