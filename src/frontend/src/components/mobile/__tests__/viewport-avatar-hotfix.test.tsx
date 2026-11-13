import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { measureKeyboardInset } from "@/hooks/use-keyboard-inset";
import { AccountInitial } from "../account-initial";

// Hotfix 1.12.2: on Android Chrome the /m screen collapsed to its app bar, and the avatar showed «؟».
describe("measureKeyboardInset (hotfix 1.12.2)", () => {
  const unzoomed = { height: 873, offsetTop: 0, scale: 1 };

  it("reports no keyboard while no text field has focus, whatever the viewport says", () => {
    expect(measureKeyboardInset({ height: 400, offsetTop: 0, scale: 1 }, 873, false)).toBe(0);
  });

  it("does not treat pinch zoom as a keyboard", () => {
    expect(measureKeyboardInset({ height: 546, offsetTop: 120, scale: 1.6 }, 873, true)).toBe(0);
  });

  it("ignores browser-chrome differences below the keyboard threshold", () => {
    expect(measureKeyboardInset({ height: 820, offsetTop: 0, scale: 1 }, 873, true)).toBe(0);
    expect(measureKeyboardInset(unzoomed, 873, true)).toBe(0);
  });

  it("lifts docked content above an open keyboard to the bottom of the visible area", () => {
    expect(measureKeyboardInset({ height: 500, offsetTop: 0, scale: 1 }, 873, true)).toBe(373);
    expect(measureKeyboardInset({ height: 500, offsetTop: 100, scale: 1 }, 873, true)).toBe(273);
  });
});

describe("AccountInitial (hotfix 1.12.2)", () => {
  it("shows a person icon, never «؟», when there is no display name", () => {
    const { container } = render(<AccountInitial name={null} />);
    const badge = container.querySelector("[data-account-initial]");
    expect(badge?.getAttribute("data-account-initial")).toBe("icon");
    expect(badge?.querySelector("svg")).not.toBeNull();
    expect(container.textContent).not.toContain("؟");
  });

  it("shows the first letter of the display name", () => {
    const { container } = render(<AccountInitial name="کامران" />);
    expect(
      container.querySelector("[data-account-initial]")?.getAttribute("data-account-initial"),
    ).toBe("letter");
    expect(container.textContent).toBe("ک");
  });
});
