import { describe, expect, it } from "vitest";
import {
  isMobilePath,
  toDesktopPath,
  toMobileAutoPath,
  toMobilePath,
} from "@/lib/experience/path-map";
import {
  parseExperienceOverride,
  parseExperiencePreference,
  serializeExperienceCookie,
  withoutExperienceOverride,
  type Experience,
} from "@/lib/experience/preference";
import {
  isPhoneUserAgent,
  parseMobileClientHint,
  resolveExperience,
} from "@/lib/experience/resolve-experience";
import { automaticMobileRedirect } from "@/lib/experience/redirect";

const USER_AGENTS = {
  iphoneSafari:
    "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1",
  androidChromePhone:
    "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36",
  androidChromeTablet:
    "Mozilla/5.0 (Linux; Android 14; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
  ipadOs:
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15",
  samsungInternetPhone:
    "Mozilla/5.0 (Linux; Android 14; SAMSUNG SM-S918B) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/25.0 Chrome/121.0.0.0 Mobile Safari/537.36",
  firefoxAndroid: "Mozilla/5.0 (Android 14; Mobile; rv:131.0) Gecko/131.0 Firefox/131.0",
  desktopChrome:
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
  desktopSafari:
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15",
  desktopEdge:
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0",
  googlebotDesktop:
    "Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; Googlebot/2.1; +http://www.google.com/bot.html) Chrome/130.0.0.0 Safari/537.36",
  googlebotSmartphone:
    "Mozilla/5.0 (Linux; Android 6.0.1; Nexus 5X Build/MMB29P) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)",
  empty: "",
} as const;

describe("isPhoneUserAgent (spec §7.1)", () => {
  it.each([
    ["iphoneSafari", true],
    ["androidChromePhone", true],
    ["samsungInternetPhone", true],
    ["firefoxAndroid", true],
    ["androidChromeTablet", false],
    ["ipadOs", false],
    ["desktopChrome", false],
    ["desktopSafari", false],
    ["desktopEdge", false],
    ["googlebotDesktop", false],
    ["googlebotSmartphone", false],
    ["empty", false],
  ] as const)("%s → %s", (name, expected) => {
    expect(isPhoneUserAgent(USER_AGENTS[name])).toBe(expected);
  });
});

describe("resolveExperience", () => {
  it("reads the Sec-CH-UA-Mobile client hint", () => {
    expect(parseMobileClientHint("?1")).toBe(true);
    expect(parseMobileClientHint("?0")).toBe(false);
    expect(parseMobileClientHint("1")).toBeNull();
    expect(parseMobileClientHint(null)).toBeNull();
  });

  it("lets an explicit preference win over every signal", () => {
    expect(
      resolveExperience({
        preference: "desktop",
        uaMobileHint: true,
        userAgent: USER_AGENTS.iphoneSafari,
      }),
    ).toBe("desktop");
    expect(
      resolveExperience({
        preference: "mobile",
        uaMobileHint: false,
        userAgent: USER_AGENTS.desktopChrome,
      }),
    ).toBe("mobile");
  });

  it("uses the client hint, then the user agent, then defaults to desktop", () => {
    expect(resolveExperience({ preference: null, uaMobileHint: true, userAgent: "" })).toBe(
      "mobile",
    );
    expect(
      resolveExperience({
        preference: null,
        uaMobileHint: null,
        userAgent: USER_AGENTS.iphoneSafari,
      }),
    ).toBe("mobile");
    expect(
      resolveExperience({ preference: null, uaMobileHint: null, userAgent: USER_AGENTS.ipadOs }),
    ).toBe("desktop");
    expect(resolveExperience({ preference: null, uaMobileHint: null, userAgent: undefined })).toBe(
      "desktop",
    );
  });
});

describe("path mapping (spec §7.2)", () => {
  it("maps the three auto-redirect paths, preserving search and hash", () => {
    expect(toMobileAutoPath("/chat")).toBe("/m");
    expect(toMobileAutoPath("/chat/")).toBe("/m");
    expect(toMobileAutoPath("/c/abc?x=1#end")).toBe("/m/c/abc?x=1#end");
    expect(toMobileAutoPath("/followed-symbols#symbol-search")).toBe("/m/watchlist#symbol-search");
  });

  it.each([
    "/",
    "/auth",
    "/auth?redirect=%2Fchat",
    "/launch?code=x",
    "/launch-failed",
    "/verify-email",
    "/verify-enrollment",
    "/reset-password",
    "/forgot-password",
    "/profile",
    "/telegram-link",
    "/top-up",
    "/disclosures",
    "/getCompanyId",
    "/admin",
    "/admin/data",
    "/m",
    "/m/c/abc",
    "/c/",
    "/c/a/b",
    "/_serverFn/abc",
    "/assets/index.js",
  ])("never auto-redirects %s", (href) => {
    expect(toMobileAutoPath(href)).toBeNull();
  });

  it("maps manual switches in both directions", () => {
    expect(toMobilePath("/profile")).toBe("/m/account/profile");
    expect(toMobilePath("/disclosures")).toBe("/m");
    expect(toDesktopPath("/m/c/abc?x=1")).toBe("/c/abc?x=1");
    expect(toDesktopPath("/m/watchlist")).toBe("/followed-symbols");
    expect(toDesktopPath("/m/account")).toBe("/profile");
    expect(toDesktopPath("/m/account/profile")).toBe("/profile");
    expect(toDesktopPath("/m/history")).toBe("/chat");
    expect(toDesktopPath("/m/market")).toBe("/chat");
    expect(toDesktopPath("/m")).toBe("/chat");
  });

  it("recognizes mobile paths", () => {
    expect(isMobilePath("/m")).toBe(true);
    expect(isMobilePath("/m/history/")).toBe(true);
    expect(isMobilePath("/market")).toBe(false);
  });
});

describe("preference cookie and override", () => {
  it("parses only known values", () => {
    expect(parseExperiencePreference("a=1; sapio_experience=mobile; b=2")).toBe("mobile");
    expect(parseExperiencePreference("sapio_experience=tablet")).toBeNull();
    expect(parseExperiencePreference(undefined)).toBeNull();
  });

  it("serializes a script-readable, year-long, Lax cookie; Secure outside loopback", () => {
    expect(serializeExperienceCookie("desktop", { secure: true })).toBe(
      "sapio_experience=desktop; Path=/; Max-Age=31536000; SameSite=Lax; Secure",
    );
    expect(serializeExperienceCookie("mobile", { secure: false })).not.toContain("Secure");
    expect(serializeExperienceCookie("mobile", { secure: true })).not.toContain("HttpOnly");
  });

  it("ignores an unsafe ?experience= value", () => {
    expect(parseExperienceOverride("?experience=mobile")).toBe("mobile");
    expect(parseExperienceOverride("?experience=%3Cscript%3E")).toBeNull();
    expect(parseExperienceOverride("?experience=")).toBeNull();
    expect(withoutExperienceOverride("?experience=mobile&x=1")).toBe("?x=1");
    expect(withoutExperienceOverride("?experience=mobile")).toBe("");
  });
});

describe("automaticMobileRedirect", () => {
  const phone = { preference: null, uaMobileHint: null, userAgent: USER_AGENTS.iphoneSafari };

  it("does nothing while the rollout flag is off", () => {
    expect(automaticMobileRedirect("/chat", phone, { enabled: false })).toBeNull();
  });

  it("redirects phones from mapped paths only", () => {
    expect(automaticMobileRedirect("/chat", phone, { enabled: true })).toEqual({
      target: "/m",
      reason: "ua",
    });
    expect(automaticMobileRedirect("/profile", phone, { enabled: true })).toBeNull();
  });

  it("is idempotent for every combination of signals and paths (AC-22)", () => {
    const preferences: Array<Experience | null> = [null, "mobile", "desktop"];
    const hints = [null, true, false];
    const hrefs = [
      "/chat",
      "/c/abc?x=1",
      "/followed-symbols",
      "/profile",
      "/",
      "/auth",
      "/m",
      "/m/c/abc",
      "/admin",
    ];
    for (const preference of preferences)
      for (const uaMobileHint of hints)
        for (const userAgent of Object.values(USER_AGENTS))
          for (const href of hrefs) {
            const signals = { preference, uaMobileHint, userAgent };
            const first = automaticMobileRedirect(href, signals, { enabled: true });
            const next = first ? first.target : href;
            const second = automaticMobileRedirect(next, signals, { enabled: true });
            expect(
              second,
              `${href} with ${JSON.stringify({ preference, uaMobileHint })}`,
            ).toBeNull();
          }
  });
});
