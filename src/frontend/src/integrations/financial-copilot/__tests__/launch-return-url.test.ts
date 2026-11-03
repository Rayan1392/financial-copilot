import { describe, expect, it } from "vitest";
import { validateTahlilAppReturnUrl } from "../launch";

describe("validateTahlilAppReturnUrl", () => {
  it.each([undefined, "", "   "])("hides the button when the value is %j", (value) => {
    expect(validateTahlilAppReturnUrl(value, { isDev: false })).toBeNull();
    expect(validateTahlilAppReturnUrl(value, { isDev: true })).toBeNull();
  });

  it("accepts an absolute https URL in every mode", () => {
    const url = "https://tahlilapp.example/return";
    expect(validateTahlilAppReturnUrl(url, { isDev: false })).toBe(url);
    expect(validateTahlilAppReturnUrl(url, { isDev: true })).toBe(url);
  });

  it("returns the trimmed configured value", () => {
    expect(validateTahlilAppReturnUrl("  https://tahlilapp.example/return  ", { isDev: false })).toBe(
      "https://tahlilapp.example/return",
    );
  });

  it("rejects plain http for non-local hosts, even in development", () => {
    expect(validateTahlilAppReturnUrl("http://tahlilapp.example/return", { isDev: false })).toBeNull();
    expect(validateTahlilAppReturnUrl("http://tahlilapp.example/return", { isDev: true })).toBeNull();
  });

  it("accepts http only for localhost while developing", () => {
    expect(validateTahlilAppReturnUrl("http://localhost:3000/back", { isDev: true })).toBe(
      "http://localhost:3000/back",
    );
    expect(validateTahlilAppReturnUrl("http://localhost:3000/back", { isDev: false })).toBeNull();
    expect(validateTahlilAppReturnUrl("http://127.0.0.1:3000/back", { isDev: true })).toBeNull();
  });

  it("rejects URLs that carry credentials", () => {
    expect(validateTahlilAppReturnUrl("https://reviewer@tahlilapp.example/return", { isDev: false })).toBeNull();
    expect(validateTahlilAppReturnUrl("https://reviewer:x@tahlilapp.example/return", { isDev: false })).toBeNull();
  });

  it("rejects relative, scheme-less, and non-web URLs", () => {
    expect(validateTahlilAppReturnUrl("/return", { isDev: true })).toBeNull();
    expect(validateTahlilAppReturnUrl("tahlilapp.example/return", { isDev: true })).toBeNull();
    expect(validateTahlilAppReturnUrl("javascript:alert(1)", { isDev: true })).toBeNull();
    expect(validateTahlilAppReturnUrl("ftp://tahlilapp.example/return", { isDev: true })).toBeNull();
    expect(validateTahlilAppReturnUrl("not a url", { isDev: true })).toBeNull();
  });
});
