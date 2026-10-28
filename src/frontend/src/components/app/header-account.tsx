import { Link } from "@tanstack/react-router";
import { useQuery } from "@tanstack/react-query";
import { getOwnProfile } from "@/integrations/financial-copilot/auth";
import { officialDisplayName } from "@/lib/profile-display-name";

/** Shared cache key for `GET /api/auth/v1/profile`. AuthSync clears the cache on login/logout. */
export const OWN_PROFILE_QUERY_KEY = ["own-profile"] as const;

/**
 * Compact account chip at the start (right, in RTL) of the main header: avatar initial and the TahlilApp
 * display name, linking to the profile page. Renders nothing while loading, on error (for example a user with no
 * TahlilApp profile), or when no usable display name exists, so no empty placeholder is ever shown.
 */
export function HeaderAccount() {
  const { data: displayName } = useQuery({
    queryKey: OWN_PROFILE_QUERY_KEY,
    queryFn: getOwnProfile,
    select: (profile) => officialDisplayName(profile.displayName),
    // The name changes only on a new TahlilApp launch, which issues a new session and clears the cache.
    staleTime: Infinity,
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });

  if (!displayName) return null;

  return (
    <div className="flex min-w-0 items-center gap-3">
      <Link
        to="/profile"
        aria-label={`پروفایل حساب: ${displayName}`}
        title={displayName}
        className="flex min-w-0 items-center gap-2 rounded-lg py-1 pe-2 ps-1 text-muted-foreground transition hover:bg-surface hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
      >
        <span
          aria-hidden="true"
          className="flex size-7 shrink-0 items-center justify-center rounded-full bg-emerald-soft text-xs font-semibold text-emerald ring-1 ring-emerald/30"
        >
          {Array.from(displayName)[0]}
        </span>
        <span
          data-testid="header-account-name"
          className="hidden max-w-40 truncate text-sm font-medium sm:block lg:max-w-56"
        >
          {displayName}
        </span>
      </Link>
      <span aria-hidden="true" className="hidden h-5 w-px bg-hairline sm:block" />
    </div>
  );
}
