import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { OwnProfile } from "@/integrations/financial-copilot/auth";

const getOwnProfile = vi.fn<() => Promise<OwnProfile>>();

vi.mock("@/integrations/financial-copilot/auth", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/integrations/financial-copilot/auth")>()),
  getOwnProfile: () => getOwnProfile(),
}));

const { TahlilAppProfileCard } = await import("@/routes/_app.profile");

const emptyProfile: OwnProfile = {
  displayName: null,
  firstName: null,
  lastName: null,
  nationalIdMasked: null,
  mobileMasked: null,
  profileSyncedAtUtc: null,
};

async function renderedName(profile: OwnProfile) {
  getOwnProfile.mockResolvedValue(profile);
  render(<TahlilAppProfileCard />);
  const label = await screen.findByText("نام و نام خانوادگی");
  return label.nextElementSibling?.textContent;
}

describe("TahlilApp profile card name", () => {
  afterEach(() => {
    getOwnProfile.mockReset();
  });

  it("shows the official TahlilApp display name", async () => {
    expect(
      await renderedName({ ...emptyProfile, displayName: "کامران عبدی", firstName: "Ali" }),
    ).toBe("کامران عبدی");
  });

  it("falls back to first and last name for older records", async () => {
    expect(await renderedName({ ...emptyProfile, firstName: "Ali", lastName: "Rezaei" })).toBe(
      "Ali Rezaei",
    );
  });

  it("shows «—», never 'null null', when no name was supplied", async () => {
    const name = await renderedName(emptyProfile);

    expect(name).toBe("—");
    expect(screen.queryByText(/null/)).not.toBeInTheDocument();
  });
});
