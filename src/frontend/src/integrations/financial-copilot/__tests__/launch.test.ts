import { afterEach, describe, expect, it, vi } from "vitest";
import { completeTahlilAppLaunch, FinancialCopilotAuthError } from "../auth";
import {
  extractLaunchCode,
  launchFailureReasonFor,
  parseLaunchFailureReason,
} from "../launch";

const VALID_CODE = "AbCdEfGhIjKlMnOpQrStUvWxYz0123-_";

describe("launch code extraction", () => {
  it("accepts a well-formed opaque code from the query string", () => {
    expect(extractLaunchCode(`?code=${VALID_CODE}`)).toBe(VALID_CODE);
  });

  it("rejects absent, short, or malformed codes before any request", () => {
    expect(extractLaunchCode("")).toBeNull();
    expect(extractLaunchCode("?code=short")).toBeNull();
    expect(extractLaunchCode("?code=has%20spaces%20in%20the%20code!!")).toBeNull();
    expect(extractLaunchCode("?code=../../etc/passwd-and-more")).toBeNull();
  });

  it("ignores identity-like parameters; only the code is read", () => {
    expect(extractLaunchCode(`?userId=42&code=${VALID_CODE}`)).toBe(VALID_CODE);
  });
});

describe("launch failure reasons", () => {
  it("maps each ProblemDetails type to a closed reason", () => {
    expect(launchFailureReasonFor("https://financialcopilot/errors/launch-invalid")).toBe("invalid");
    expect(launchFailureReasonFor("https://financialcopilot/errors/launch-unavailable")).toBe("unavailable");
    expect(launchFailureReasonFor("https://financialcopilot/errors/launch-account-disabled")).toBe("disabled");
    expect(launchFailureReasonFor("https://financialcopilot/errors/anything-else")).toBe("unknown");
    expect(launchFailureReasonFor(undefined)).toBe("unknown");
  });

  it("never reflects arbitrary search input as a reason", () => {
    expect(parseLaunchFailureReason("unavailable")).toBe("unavailable");
    expect(parseLaunchFailureReason("<script>alert(1)</script>")).toBe("unknown");
    expect(parseLaunchFailureReason(["invalid"])).toBe("unknown");
    expect(parseLaunchFailureReason(undefined)).toBe("unknown");
  });
});

describe("completeTahlilAppLaunch", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    sessionStorage.clear();
  });

  it("posts only the code in the body, with credentials, and stores the session token", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({
        accessToken: "access-token-1",
        accessTokenExpiresAt: "2026-10-18T00:00:00Z",
        user: { userId: "u-1", email: "", tenantId: "t-1", roles: ["User"], permissions: [] },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    ));
    vi.stubGlobal("fetch", fetchMock);

    const token = await completeTahlilAppLaunch(VALID_CODE);

    expect(token).toBe("access-token-1");
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("/api/auth/v1/tahlilapp/launch");
    expect(init?.credentials).toBe("include");
    expect(JSON.parse(String(init?.body))).toEqual({ code: VALID_CODE });
    expect(sessionStorage.getItem("financial_copilot_access_token")).toBe("access-token-1");
  });

  it("surfaces the problem type and correlation id without storing a session", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(
      JSON.stringify({
        type: "https://financialcopilot/errors/launch-invalid",
        title: "The launch request is invalid or has expired.",
        correlationId: "corr-7",
      }),
      { status: 401, headers: { "Content-Type": "application/problem+json" } },
    )));

    const error = await completeTahlilAppLaunch(VALID_CODE).catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(FinancialCopilotAuthError);
    expect((error as FinancialCopilotAuthError).type).toBe("https://financialcopilot/errors/launch-invalid");
    expect((error as FinancialCopilotAuthError).correlationId).toBe("corr-7");
    expect(sessionStorage.getItem("financial_copilot_access_token")).toBeNull();
  });
});
