import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const ACCESS_TOKEN_KEY = "financial_copilot_access_token";
const PROBLEM_PREFIX = "https://financialcopilot/errors/";

type AuthModule = typeof import("../auth");

// Builds an unsigned, test-only JWT with the claims the client reads. It is not a credential.
function testAccessToken(expiresInSeconds: number): string {
  const encode = (value: object) =>
    btoa(JSON.stringify(value)).replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
  const exp = Math.floor(Date.now() / 1000) + expiresInSeconds;
  return `${encode({ alg: "none" })}.${encode({ sub: "user-1", exp, "financial_copilot:tenant_id": "tenant-1" })}.sig`;
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function problemResponse(status: number, name: string) {
  return new Response(
    JSON.stringify({ type: `${PROBLEM_PREFIX}${name}`, title: "Request rejected.", correlationId: "corr-test-1" }),
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

function requestAt(fetchMock: ReturnType<typeof vi.fn>, index: number) {
  const [url, init] = fetchMock.mock.calls[index] as [string | URL, RequestInit | undefined];
  return { url: String(url), init: init ?? {} };
}

function headerOf(init: RequestInit, name: string) {
  return new Headers(init.headers).get(name);
}

describe("account, credential, and profile calls", () => {
  let auth: AuthModule;

  beforeEach(async () => {
    // Fresh module state per test: the in-memory access token must not leak between cases.
    vi.resetModules();
    sessionStorage.clear();
    sessionStorage.setItem(ACCESS_TOKEN_KEY, testAccessToken(3600));
    auth = await import("../auth");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    sessionStorage.clear();
  });

  it("getAccountSummary sends a bearer GET with credentials and returns the typed summary", async () => {
    const summary = {
      email: "user@example.test",
      emailConfirmed: true,
      hasPassword: false,
      hasTahlilAppIdentity: true,
      accountAuthState: "B",
    };
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(summary));
    vi.stubGlobal("fetch", fetchMock);

    await expect(auth.getAccountSummary()).resolves.toEqual(summary);

    const { url, init } = requestAt(fetchMock, 0);
    expect(url).toContain("/api/auth/v1/account");
    expect(init.method).toBe("GET");
    expect(init.credentials).toBe("include");
    expect(init.cache).toBe("no-store");
    expect(headerOf(init, "Authorization")).toBe(`Bearer ${sessionStorage.getItem(ACCESS_TOKEN_KEY)}`);
    expect(init.body).toBeUndefined();
  });

  it("requestEmailEnrollment posts only the email and returns the accepted message", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ message: "Accepted." }, 202));
    vi.stubGlobal("fetch", fetchMock);

    await expect(auth.requestEmailEnrollment("new@example.test")).resolves.toEqual({ message: "Accepted." });

    const { url, init } = requestAt(fetchMock, 0);
    expect(url).toContain("/api/auth/v1/credentials/email");
    expect(init.method).toBe("POST");
    expect(init.credentials).toBe("include");
    expect(headerOf(init, "Content-Type")).toBe("application/json");
    expect(JSON.parse(String(init.body))).toEqual({ email: "new@example.test" });
  });

  it("requestEmailEnrollment maps a 503 delivery outage to FinancialCopilotAuthError", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(problemResponse(503, "email-delivery-unavailable")));

    const error = await auth.requestEmailEnrollment("new@example.test").catch((reason: unknown) => reason);

    expect(error).toBeInstanceOf(auth.FinancialCopilotAuthError);
    expect(error).toMatchObject({
      status: 503,
      type: `${PROBLEM_PREFIX}email-delivery-unavailable`,
      correlationId: "corr-test-1",
    });
  });

  it("requestEmailEnrollment maps a malformed address (400) to FinancialCopilotAuthError", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(problemResponse(400, "validation-failed")));

    await expect(auth.requestEmailEnrollment("not-an-address")).rejects.toMatchObject({ status: 400 });
  });

  it("confirmEmailEnrollment posts the token in the body and accepts a 204 reply", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(auth.confirmEmailEnrollment("enrollment-token")).resolves.toBeUndefined();

    const { url, init } = requestAt(fetchMock, 0);
    expect(url).toContain("/api/auth/v1/credentials/email/confirm");
    expect(init.method).toBe("POST");
    expect(init.credentials).toBe("include");
    expect(JSON.parse(String(init.body))).toEqual({ token: "enrollment-token" });
  });

  it("confirmEmailEnrollment maps an invalid or expired token (400)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(problemResponse(400, "invalid-or-expired-token")));

    await expect(auth.confirmEmailEnrollment("stale-token")).rejects.toMatchObject({
      status: 400,
      type: `${PROBLEM_PREFIX}invalid-or-expired-token`,
    });
  });

  it("confirmEmailEnrollment maps an address already owned elsewhere (409)", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(problemResponse(409, "email-already-registered")));

    const error = await auth.confirmEmailEnrollment("enrollment-token").catch((reason: unknown) => reason);

    expect(error).toBeInstanceOf(auth.FinancialCopilotAuthError);
    expect(error).toMatchObject({ status: 409, type: `${PROBLEM_PREFIX}email-already-registered` });
  });

  it("setAccountPassword posts only the new password and accepts a 204 reply", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(auth.setAccountPassword("placeholder-password")).resolves.toBeUndefined();

    const { url, init } = requestAt(fetchMock, 0);
    expect(url).toContain("/api/auth/v1/credentials/password");
    expect(init.method).toBe("POST");
    expect(init.credentials).toBe("include");
    expect(JSON.parse(String(init.body))).toEqual({ newPassword: "placeholder-password" });
  });

  it.each([
    [400, "password-rejected"],
    [409, "password-already-set"],
    [409, "email-not-confirmed"],
  ])("setAccountPassword maps %i %s to FinancialCopilotAuthError", async (status, name) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(problemResponse(status, name)));

    await expect(auth.setAccountPassword("placeholder-password")).rejects.toMatchObject({
      status,
      type: `${PROBLEM_PREFIX}${name}`,
    });
  });

  it("getOwnProfile returns masked fields from a no-store GET", async () => {
    const profile = {
      firstName: "Test",
      lastName: "Person",
      nationalIdMasked: "***-***-**89",
      mobileMasked: "0912***4567",
      profileSyncedAtUtc: null,
    };
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(profile));
    vi.stubGlobal("fetch", fetchMock);

    await expect(auth.getOwnProfile()).resolves.toEqual(profile);

    const { url, init } = requestAt(fetchMock, 0);
    expect(url).toContain("/api/auth/v1/profile");
    expect(init.method).toBe("GET");
    expect(init.cache).toBe("no-store");
  });

  it("revealOwnProfileField posts the field name only and returns the single value", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse({ field: "mobile", value: "placeholder-value" }));
    vi.stubGlobal("fetch", fetchMock);

    await expect(auth.revealOwnProfileField("mobile")).resolves.toEqual({
      field: "mobile",
      value: "placeholder-value",
    });

    const { url, init } = requestAt(fetchMock, 0);
    expect(url).toContain("/api/auth/v1/profile/reveal");
    expect(init.method).toBe("POST");
    expect(init.cache).toBe("no-store");
    expect(JSON.parse(String(init.body))).toEqual({ field: "mobile" });
  });

  it("refreshes an expiring access token before the authorized call", async () => {
    sessionStorage.setItem(ACCESS_TOKEN_KEY, testAccessToken(5));
    const refreshedToken = testAccessToken(3600);
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        jsonResponse({
          accessToken: refreshedToken,
          accessTokenExpiresAt: "2099-01-01T00:00:00Z",
          user: { userId: "user-1", email: "", tenantId: "tenant-1", roles: [], permissions: [] },
        }),
      )
      .mockResolvedValueOnce(jsonResponse({ hasPassword: false }));
    vi.stubGlobal("fetch", fetchMock);

    await auth.getAccountSummary();

    expect(requestAt(fetchMock, 0).url).toContain("/api/auth/v1/refresh");
    expect(headerOf(requestAt(fetchMock, 1).init, "Authorization")).toBe(`Bearer ${refreshedToken}`);
  });

  it("reports 401 without calling the API when no session can be refreshed", async () => {
    sessionStorage.clear();
    const fetchMock = vi.fn().mockResolvedValue(problemResponse(401, "unauthenticated"));
    vi.stubGlobal("fetch", fetchMock);

    await expect(auth.getOwnProfile()).rejects.toMatchObject({ status: 401 });

    // Only the refresh attempt is made; the authorized profile call is never sent.
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(requestAt(fetchMock, 0).url).toContain("/api/auth/v1/refresh");
  });
});
