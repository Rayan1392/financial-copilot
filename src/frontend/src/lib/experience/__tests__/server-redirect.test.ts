import { describe, expect, it } from "vitest";
import { mobileRedirectResponse } from "@/lib/experience/server-redirect";

const IPHONE =
  "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";
const IPAD =
  "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15";
const DESKTOP =
  "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

function request(
  path: string,
  { method = "GET", userAgent = IPHONE, headers = {} as Record<string, string> } = {},
): Request {
  return new Request(`https://sapioai.ir${path}`, {
    method,
    headers: {
      "user-agent": userAgent,
      "sec-fetch-mode": "navigate",
      accept: "text/html",
      ...headers,
    },
  });
}

const enabled = { enabled: true };

describe("mobileRedirectResponse (AC-20, AC-21)", () => {
  it.each([
    ["/chat", "/m"],
    [
      "/c/0f8fad5b-d9cb-469f-a165-70867728950e?x=1",
      "/m/c/0f8fad5b-d9cb-469f-a165-70867728950e?x=1",
    ],
    ["/followed-symbols", "/m/watchlist"],
  ])("redirects an iPhone navigation to %s with 302 → %s", (path, location) => {
    const response = mobileRedirectResponse(request(path), enabled);
    expect(response?.status).toBe(302);
    expect(response?.headers.get("location")).toBe(location);
    expect(response?.headers.get("cache-control")).toBe("private, no-store");
    expect(response?.headers.get("vary")).toBe("Sec-CH-UA-Mobile, User-Agent, Cookie");
    expect(response?.headers.get("x-sapio-experience")).toBe("mobile; reason=ua");
  });

  it("redirects on the mobile client hint", () => {
    const response = mobileRedirectResponse(
      request("/chat", { userAgent: DESKTOP, headers: { "sec-ch-ua-mobile": "?1" } }),
      enabled,
    );
    expect(response?.headers.get("x-sapio-experience")).toBe("mobile; reason=ch");
  });

  it.each([
    ["the flag is off", request("/chat"), { enabled: false }],
    ["a desktop browser", request("/chat", { userAgent: DESKTOP }), enabled],
    ["an iPad", request("/chat", { userAgent: IPAD }), enabled],
    [
      "a desktop preference",
      request("/chat", { headers: { cookie: "sapio_experience=desktop" } }),
      enabled,
    ],
    ["a POST", request("/chat", { method: "POST" }), enabled],
    ["a server function", request("/_serverFn/abc"), enabled],
    [
      "a fetch (not a navigation)",
      request("/chat", { headers: { "sec-fetch-mode": "cors" } }),
      enabled,
    ],
    [
      "an asset",
      request("/assets/index-abc.js", { headers: { "sec-fetch-mode": "no-cors" } }),
      enabled,
    ],
  ])("does not redirect %s", (_name, req, options) => {
    expect(mobileRedirectResponse(req, options)).toBeNull();
  });

  it.each([
    "/",
    "/auth",
    "/launch?code=abc",
    "/profile",
    "/telegram-link",
    "/admin",
    "/top-up",
    "/disclosures",
    "/m",
  ])("never redirects the excluded path %s", (path) => {
    expect(mobileRedirectResponse(request(path), enabled)).toBeNull();
  });

  it("treats Accept: text/html as a navigation when Fetch Metadata is absent", () => {
    const req = new Request("https://sapioai.ir/chat", {
      headers: { "user-agent": IPHONE, accept: "text/html" },
    });
    expect(mobileRedirectResponse(req, enabled)?.status).toBe(302);
  });

  it("stores an ?experience= override and drops the parameter", () => {
    const response = mobileRedirectResponse(request("/chat?experience=desktop&x=1"), enabled);
    expect(response?.status).toBe(302);
    expect(response?.headers.get("location")).toBe("/chat?x=1");
    expect(response?.headers.get("set-cookie")).toContain("sapio_experience=desktop");
    expect(response?.headers.get("set-cookie")).toContain("Secure");
  });
});
