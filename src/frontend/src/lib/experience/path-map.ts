/**
 * The single owner of desktop ↔ mobile path mapping (Feature 153 spec §7.2). Pure: no framework imports.
 *
 * Automatic redirects only ever map desktop → mobile (`toMobileAutoPath`), and no `/m` path is a source, so
 * applying the redirect twice equals applying it once. The reverse direction is used only by the manual switch.
 */

const THREAD_ID = /^[^/]+$/;

function normalizePathname(pathname: string): string {
  if (pathname.length > 1 && pathname.endsWith("/")) return pathname.replace(/\/+$/, "") || "/";
  return pathname;
}

function splitHref(href: string): { pathname: string; suffix: string } {
  const index = href.search(/[?#]/);
  return index === -1
    ? { pathname: normalizePathname(href), suffix: "" }
    : { pathname: normalizePathname(href.slice(0, index)), suffix: href.slice(index) };
}

/** Desktop paths that phones are redirected away from automatically (when the rollout flag is on). */
function autoMobilePathname(pathname: string): string | null {
  if (pathname === "/chat") return "/m";
  if (pathname === "/followed-symbols") return "/m/watchlist";
  const thread = pathname.match(/^\/c\/(.+)$/);
  if (thread && THREAD_ID.test(thread[1])) return `/m/c/${thread[1]}`;
  return null;
}

/**
 * Maps a desktop href (path + optional search + hash) to its mobile equivalent for an **automatic** redirect,
 * preserving search and hash. Returns null for every path that must never be redirected (`/`, `/auth`,
 * `/launch*`, `/profile`, `/admin*`, and anything else outside the table).
 */
export function toMobileAutoPath(href: string): string | null {
  const { pathname, suffix } = splitHref(href);
  const target = autoMobilePathname(pathname);
  return target === null ? null : target + suffix;
}

/** The mobile destination for the manual «نسخه موبایل» switch: the mapped path, otherwise the mobile home. */
export function toMobilePath(href: string): string {
  const { pathname } = splitHref(href);
  if (pathname === "/profile") return "/m/account/profile";
  return toMobileAutoPath(href) ?? "/m";
}

/** The desktop destination for the manual «نمایش نسخه دسکتاپ» switch, preserving search and hash. */
export function toDesktopPath(href: string): string {
  const { pathname, suffix } = splitHref(href);
  const thread = pathname.match(/^\/m\/c\/(.+)$/);
  if (thread && THREAD_ID.test(thread[1])) return `/c/${thread[1]}${suffix}`;
  if (pathname === "/m/watchlist") return `/followed-symbols${suffix}`;
  if (pathname === "/m/account" || pathname === "/m/account/profile") return "/profile";
  return "/chat";
}

export function isMobilePath(pathname: string): boolean {
  const normalized = normalizePathname(pathname);
  return normalized === "/m" || normalized.startsWith("/m/");
}
