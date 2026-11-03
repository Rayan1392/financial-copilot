import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const IPHONE_SAFARI =
  "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";
const FIREFOX_ANDROID = "Mozilla/5.0 (Android 14; Mobile; rv:131.0) Gecko/131.0 Firefox/131.0";
const CHROME_ANDROID =
  "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Mobile Safari/537.36";

function environment({
  userAgent,
  standalone = false,
}: {
  userAgent: string;
  standalone?: boolean;
}) {
  vi.spyOn(navigator, "userAgent", "get").mockReturnValue(userAgent);
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: query === "(display-mode: standalone)" ? standalone : false,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

async function loadCard() {
  vi.resetModules();
  return (await import("../install-card")).InstallCard;
}

beforeEach(() => {
  window.localStorage.clear();
  window.localStorage.setItem("sapio-has-answer", "1");
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("InstallCard (T20)", () => {
  it("Chromium with a captured install prompt shows «نصب» and calls the prompt", async () => {
    environment({ userAgent: CHROME_ANDROID });
    const InstallCard = await loadCard();
    render(<InstallCard />);
    const prompt = vi.fn(async () => undefined);
    const event = Object.assign(new Event("beforeinstallprompt", { cancelable: true }), {
      prompt,
      userChoice: Promise.resolve({ outcome: "accepted" }),
    });
    act(() => {
      window.dispatchEvent(event);
    });
    const install = await screen.findByRole("button", { name: "نصب" });
    expect(event.defaultPrevented).toBe(true);
    await act(async () => install.click());
    expect(prompt).toHaveBeenCalledOnce();
  });

  it("iOS Safari shows «نصب», which opens the Add to Home Screen steps", async () => {
    environment({ userAgent: IPHONE_SAFARI });
    const InstallCard = await loadCard();
    render(<InstallCard />);
    const install = await screen.findByRole("button", { name: "نصب" });
    await act(async () => install.click());
    expect(await screen.findByText("افزودن ساپیو به صفحه اصلی")).toBeInTheDocument();
  });

  it("stays hidden when already installed (standalone)", async () => {
    environment({ userAgent: IPHONE_SAFARI, standalone: true });
    const InstallCard = await loadCard();
    render(<InstallCard />);
    await act(async () => undefined);
    expect(screen.queryByRole("button", { name: "نصب" })).not.toBeInTheDocument();
  });

  it("stays hidden on Firefox Android, which has no install prompt", async () => {
    environment({ userAgent: FIREFOX_ANDROID });
    const InstallCard = await loadCard();
    render(<InstallCard />);
    await act(async () => undefined);
    expect(screen.queryByRole("button", { name: "نصب" })).not.toBeInTheDocument();
  });

  it("stays hidden before the first answer and after «بعداً»", async () => {
    environment({ userAgent: IPHONE_SAFARI });
    window.localStorage.removeItem("sapio-has-answer");
    let InstallCard = await loadCard();
    const { unmount } = render(<InstallCard />);
    await act(async () => undefined);
    expect(screen.queryByRole("button", { name: "نصب" })).not.toBeInTheDocument();
    unmount();

    window.localStorage.setItem("sapio-has-answer", "1");
    InstallCard = await loadCard();
    render(<InstallCard />);
    await act(async () => (await screen.findByRole("button", { name: "بعداً" })).click());
    expect(screen.queryByRole("button", { name: "نصب" })).not.toBeInTheDocument();
    expect(Number(window.localStorage.getItem("sapio-install-snoozed"))).toBeGreaterThan(
      Date.now(),
    );
  });
});
