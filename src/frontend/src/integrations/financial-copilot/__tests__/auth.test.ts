import { afterEach, describe, expect, it, vi } from "vitest";
import { requestPasswordReset, resetPassword, verifyEmail } from "../auth";

describe("owned identity email flows", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    sessionStorage.clear();
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
