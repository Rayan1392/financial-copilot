import { describe, expect, it } from "vitest";
import {
  MISSING_PROFILE_VALUE,
  officialDisplayName,
  profileDisplayName,
} from "../profile-display-name";

describe("officialDisplayName", () => {
  it("returns the trimmed official name", () => {
    expect(officialDisplayName(" کامران عبدی  ")).toBe("کامران عبدی");
  });

  it.each([null, undefined, "", "   ", "\t\n"])("returns null for %j", (value) => {
    expect(officialDisplayName(value)).toBeNull();
  });
});

describe("profileDisplayName", () => {
  it("prefers the official TahlilApp display name", () => {
    expect(
      profileDisplayName({ displayName: "کامران عبدی", firstName: "Ali", lastName: "Rezaei" }),
    ).toBe("کامران عبدی");
  });

  it("trims the display name", () => {
    expect(
      profileDisplayName({ displayName: "  کامران عبدی ", firstName: null, lastName: null }),
    ).toBe("کامران عبدی");
  });

  it("falls back to first and last name for older records", () => {
    expect(profileDisplayName({ displayName: null, firstName: "Ali", lastName: "Rezaei" })).toBe(
      "Ali Rezaei",
    );
    expect(profileDisplayName({ displayName: "   ", firstName: "Ali", lastName: null })).toBe(
      "Ali",
    );
    expect(profileDisplayName({ firstName: null, lastName: "Rezaei" })).toBe("Rezaei");
  });

  it.each([
    { displayName: null, firstName: null, lastName: null },
    { displayName: "", firstName: " ", lastName: "" },
    {},
  ])("never renders 'null null' when no name is stored (%o)", (names) => {
    const shown = profileDisplayName(names);
    expect(shown).toBe(MISSING_PROFILE_VALUE);
    expect(shown).not.toContain("null");
    expect(shown).not.toContain("undefined");
  });
});
