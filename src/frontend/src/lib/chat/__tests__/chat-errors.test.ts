import { describe, expect, it } from "vitest";
import {
  ChatSendError,
  GENERIC_CHAT_ERROR_TEXT,
  INSUFFICIENT_CREDIT_TEXT,
  chatErrorMessage,
  classifyApiFailure,
  classifyClientError,
  isIndeterminateFailure,
} from "@/lib/chat/chat-errors";
import { formatSubscriptionCooldownDeniedMessage } from "@/lib/subscription-cooldown";

describe("classifyApiFailure", () => {
  it.each([
    [402, "insufficient-credit", "Insufficient credit for this request.", "insufficient_credit"],
    [402, "SUBSCRIPTION_COOLDOWN_ACTIVE", "anything", "cooldown"],
    [409, null, "Idempotency key conflict", "idempotency_conflict"],
    [
      409,
      null,
      "This idempotency key has already been used with a different request payload.",
      "idempotency_conflict",
    ],
    [
      409,
      "concurrency-conflict",
      "The resource changed while the request was being processed.",
      "server",
    ],
    [404, null, "Not Found", "not_found"],
    [400, null, "One or more validation errors occurred.", "validation"],
    [500, null, "The request could not be completed.", "server"],
    [503, "temporarily-unavailable", "The AI capability is temporarily unavailable.", "server"],
  ] as const)("maps %i %s to %s", (status, problemCode, message, expected) => {
    expect(classifyApiFailure(status, problemCode, message)).toBe(expected);
  });

  it("recognizes the formatted cooldown text even without the problem code", () => {
    expect(classifyApiFailure(402, null, formatSubscriptionCooldownDeniedMessage(null, 25))).toBe(
      "cooldown",
    );
  });
});

describe("classifyClientError", () => {
  it("treats a failed fetch to the server function as a network failure", () => {
    expect(classifyClientError(new TypeError("Failed to fetch")).code).toBe("network");
    expect(
      classifyClientError(new Error("NetworkError when attempting to fetch resource.")).code,
    ).toBe("network");
    expect(classifyClientError(new Error("Load failed")).code).toBe("network");
  });

  it("keeps the code of a structured failure", () => {
    expect(classifyClientError(new ChatSendError("validation", "bad")).code).toBe("validation");
  });

  it("falls back to message classification for older message-only errors", () => {
    expect(classifyClientError(new Error("insufficient credit")).code).toBe("insufficient_credit");
    expect(classifyClientError(new Error("Unauthorized: bearer token is required.")).code).toBe(
      "server",
    );
  });
});

describe("chatErrorMessage (desktop texts unchanged)", () => {
  it("shows the existing Persian texts", () => {
    expect(chatErrorMessage({ code: "insufficient_credit", message: "x" })).toBe(
      INSUFFICIENT_CREDIT_TEXT,
    );
    expect(chatErrorMessage({ code: "server", message: "x" })).toBe(GENERIC_CHAT_ERROR_TEXT);
    expect(chatErrorMessage({ code: "network", message: "x" })).toBe(GENERIC_CHAT_ERROR_TEXT);
    const cooldown = formatSubscriptionCooldownDeniedMessage(null, 25);
    expect(chatErrorMessage({ code: "cooldown", message: cooldown })).toBe(cooldown);
    expect(INSUFFICIENT_CREDIT_TEXT).toBe(
      "اعتبار کافی برای پردازش درخواست وجود ندارد. لطفاً حساب خود را شارژ کنید.",
    );
    expect(GENERIC_CHAT_ERROR_TEXT).toBe(
      "متأسفیم، خطایی در پردازش درخواست رخ داد. لطفاً دوباره امتحان کنید.",
    );
  });

  it("still classifies raw errors", () => {
    expect(chatErrorMessage(new Error("Insufficient credit"))).toBe(INSUFFICIENT_CREDIT_TEXT);
  });
});

it("only network and server failures leave the outcome unknown", () => {
  expect(isIndeterminateFailure("network")).toBe(true);
  expect(isIndeterminateFailure("server")).toBe(true);
  for (const code of [
    "insufficient_credit",
    "cooldown",
    "idempotency_conflict",
    "not_found",
    "validation",
  ] as const) {
    expect(isIndeterminateFailure(code)).toBe(false);
  }
});
