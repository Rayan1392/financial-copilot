import {
  isSecureContextHost,
  parseExperienceOverride,
  parseExperiencePreference,
  serializeExperienceCookie,
  withoutExperienceOverride,
} from "@/lib/experience/preference";
import { parseMobileClientHint } from "@/lib/experience/resolve-experience";
import { automaticMobileRedirect } from "@/lib/experience/redirect";

const NO_STORE = "private, no-store";
const VARY = "Sec-CH-UA-Mobile, User-Agent, Cookie";

/** Only top-level document navigations are redirected; fetches, server functions, and assets never are. */
function isDocumentNavigation(request: Request): boolean {
  const mode = request.headers.get("sec-fetch-mode");
  if (mode !== null) return mode === "navigate";
  return (request.headers.get("accept") ?? "").includes("text/html");
}

/**
 * Server-side half of the device routing (Feature 153 T05). Returns a 302 response, or null to let the request
 * through untouched. Everything is behind the build-time rollout flag, which defaults to off.
 */
export function mobileRedirectResponse(
  request: Request,
  { enabled }: { enabled: boolean },
): Response | null {
  if (!enabled || request.method !== "GET" || !isDocumentNavigation(request)) return null;
  const url = new URL(request.url);
  if (url.pathname.startsWith("/_serverFn")) return null;

  const override = parseExperienceOverride(url.search);
  if (override) {
    const cookie = serializeExperienceCookie(override, {
      secure: isSecureContextHost(url.protocol, url.hostname),
    });
    return redirectResponse(url.pathname + withoutExperienceOverride(url.search), {
      "Set-Cookie": cookie,
    });
  }

  const decision = automaticMobileRedirect(
    url.pathname + url.search,
    {
      preference: parseExperiencePreference(request.headers.get("cookie")),
      uaMobileHint: parseMobileClientHint(request.headers.get("sec-ch-ua-mobile")),
      userAgent: request.headers.get("user-agent"),
    },
    { enabled },
  );
  if (!decision) return null;
  return redirectResponse(decision.target, {
    "X-Sapio-Experience": `mobile; reason=${decision.reason}`,
  });
}

function redirectResponse(location: string, extraHeaders: Record<string, string>): Response {
  return new Response(null, {
    status: 302,
    headers: { Location: location, "Cache-Control": NO_STORE, Vary: VARY, ...extraHeaders },
  });
}
