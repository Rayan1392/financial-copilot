import { afterEach, describe, expect, it, vi } from "vitest";
import { register, requestPasswordReset, resetPassword, verifyEmail } from "../auth";

describe("owned identity email flows", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    sessionStorage.clear();
  });

  it("registration shows the accepted contract without creating a frontend session", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ message: "Check your email for next steps." }),
      { status: 202, headers: { "Content-Type": "application/json" } },
    ));
    vi.stubGlobal("fetch", fetchMock);

    const response = await register("new@example.test", "StrongPassword!123");

    expect(response.message).toContain("email");
    expect(fetchMock.mock.calls[0][0]).toContain("/api/auth/v1/register");
    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({
      email: "new@example.test",
      password: "StrongPassword!123",
    });
    expect(sessionStorage.getItem("financial_copilot_access_token")).toBeNull();
  });

  it("verify and reset post tokens in request bodies and accept no-content replies", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetchMock);

    await verifyEmail("verify-token");
    await resetPassword("reset-token", "NewStrongPassword!123");

    expect(JSON.parse(String(fetchMock.mock.calls[0][1]?.body))).toEqual({ token: "verify-token" });
    expect(JSON.parse(String(fetchMock.mock.calls[1][1]?.body))).toEqual({
      token: "reset-token",
      newPassword: "NewStrongPassword!123",
    });
  });

  it("forgot-password returns the same accepted response shape for the UI", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ message: "If the address is eligible, password recovery instructions will be sent." }),
      { status: 202, headers: { "Content-Type": "application/json" } },
    )));

    await expect(requestPasswordReset("user@example.test")).resolves.toHaveProperty("message");
  });
});
