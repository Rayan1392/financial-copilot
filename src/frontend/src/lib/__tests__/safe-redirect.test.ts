import { describe, expect, it } from "vitest";
import { safeInternalPath } from "../safe-redirect";

describe("safeInternalPath", () => {
  it.each(["/chat", "/c/123", "/telegram/link/confirm?token=abc", "/profile"])(
    "keeps the legitimate internal path %s",
    (value) => {
      expect(safeInternalPath(value)).toBe(value);
    },
  );

  it.each([
    "/\\evil.example",
    "//evil.example",
    "/%5Cevil.example",
    "/%2F%2Fevil.example",
    "/%255C%255Cevil.example",
    "/%2500chat",
    "https://evil.example",
    "javascript:alert(1)",
    "/\\t/evil",
    " /chat",
    "/chat\\..",
    "",
    "/%E0%A4%A",
  ])("rejects %j and falls back to /chat", (value) => {
    expect(safeInternalPath(value)).toBe("/chat");
  });

  it("falls back for missing or non-string input", () => {
    expect(safeInternalPath(undefined)).toBe("/chat");
    expect(safeInternalPath(42)).toBe("/chat");
    expect(safeInternalPath(null)).toBe("/chat");
    expect(safeInternalPath(["/profile"])).toBe("/chat");
  });

  it("uses the supplied fallback when one is given", () => {
    expect(safeInternalPath("//evil.example", "/profile")).toBe("/profile");
    expect(safeInternalPath(undefined, "/auth")).toBe("/auth");
  });
});
