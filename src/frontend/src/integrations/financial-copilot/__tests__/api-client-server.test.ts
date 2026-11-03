import { afterEach, describe, expect, it, vi } from "vitest";
import {
  FinancialCopilotApiError,
  financialCopilotServerApi,
} from "@/integrations/financial-copilot/api-client.server";
import { listThreadsInput } from "@/lib/chat.functions";

const context = { authorization: "Bearer token", correlationId: "c-1" };

function stubFetch(status: number, body: unknown) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => new Response(body === undefined ? null : JSON.stringify(body), { status })),
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("financialCopilotServerApi failures (Feature 153 T02)", () => {
  it("keeps the earlier message and adds status and problem code", async () => {
    stubFetch(402, {
      title: "insufficient-credit",
      detail: "Insufficient credit.",
      code: "INSUFFICIENT_CREDIT",
    });
    const error = await financialCopilotServerApi(context, "/api/ai/v1/query", {
      method: "POST",
      body: "{}",
    }).catch((caught: unknown) => caught);
    expect(error).toBeInstanceOf(FinancialCopilotApiError);
    expect(error).toMatchObject({
      message: "Insufficient credit.",
      status: 402,
      problemCode: "INSUFFICIENT_CREDIT",
    });
  });

  it("maps a 404 to null when the caller allows it (external company ID lookup)", async () => {
    stubFetch(404, undefined);
    await expect(
      financialCopilotServerApi(context, "/api/v1/market/external-company-id?symbol=X", {}, true),
    ).resolves.toBeNull();
  });
});

describe("listThreads input", () => {
  it("accepts no input (desktop keeps the API default) or a limit of 1-100", () => {
    expect(listThreadsInput.parse(undefined)).toBeUndefined();
    expect(listThreadsInput.parse({ limit: 100 })).toEqual({ limit: 100 });
    expect(() => listThreadsInput.parse({ limit: 0 })).toThrow();
    expect(() => listThreadsInput.parse({ limit: 101 })).toThrow();
    expect(() => listThreadsInput.parse({ limit: 2.5 })).toThrow();
  });
});
