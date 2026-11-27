import { afterEach, describe, expect, it, vi } from "vitest";
import { domToBlob } from "modern-screenshot";
import {
  canCopyImage,
  canShareFile,
  canSharePngFiles,
  comparisonImageFileName,
  gaugeImageFileName,
  renderNodeToPngBlob,
} from "@/lib/chat/answer-image";

vi.mock("modern-screenshot", () => ({ domToBlob: vi.fn() }));

const capture = vi.mocked(domToBlob);
const png = new Blob(["png"], { type: "image/png" });

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  capture.mockReset();
  for (const key of ["share", "canShare", "clipboard"] as const) {
    delete (navigator as unknown as Record<string, unknown>)[key];
  }
});

describe("renderNodeToPngBlob", () => {
  it("captures the node as a PNG at twice the CSS size", async () => {
    capture.mockResolvedValue(png);
    const node = document.createElement("div");

    await expect(renderNodeToPngBlob(Promise.resolve(node))).resolves.toBe(png);
    expect(capture).toHaveBeenCalledWith(node, { scale: 2, type: "image/png" });
  });

  it("rejects an empty picture and a picture of another type", async () => {
    capture.mockResolvedValueOnce(new Blob([], { type: "image/png" }));
    await expect(renderNodeToPngBlob(document.createElement("div"))).rejects.toThrow("empty");

    capture.mockResolvedValueOnce(new Blob(["x"], { type: "text/plain" }));
    await expect(renderNodeToPngBlob(document.createElement("div"))).rejects.toThrow("empty");
  });

  it("gives up after 15 seconds, also while the node is still mounting", async () => {
    vi.useFakeTimers();
    const never = new Promise<HTMLElement>(() => {});
    const result = renderNodeToPngBlob(never);
    const outcome = expect(result).rejects.toThrow("in time");

    await vi.advanceTimersByTimeAsync(15_000);
    await outcome;
    expect(capture).not.toHaveBeenCalled();
  });
});

describe("browser support checks", () => {
  it("copies images only with ClipboardItem and clipboard.write", () => {
    expect(canCopyImage()).toBe(false);

    vi.stubGlobal("ClipboardItem", class {});
    expect(canCopyImage()).toBe(false);

    Object.defineProperty(navigator, "clipboard", {
      value: { write: vi.fn() },
      configurable: true,
    });
    expect(canCopyImage()).toBe(true);
  });

  it("shares files only when the share sheet accepts them", () => {
    const file = new File(["x"], "a.png", { type: "image/png" });
    expect(canShareFile(file)).toBe(false);

    Object.defineProperty(navigator, "share", { value: vi.fn(), configurable: true });
    expect(canShareFile(file)).toBe(false);

    const canShare = vi.fn().mockReturnValue(true);
    Object.defineProperty(navigator, "canShare", { value: canShare, configurable: true });
    expect(canShareFile(file)).toBe(true);
    expect(canShare).toHaveBeenCalledWith({ files: [file] });
    expect(canSharePngFiles()).toBe(true);

    canShare.mockImplementation(() => {
      throw new TypeError("unsupported");
    });
    expect(canShareFile(file)).toBe(false);
  });
});

describe("gaugeImageFileName", () => {
  it("names the picture by symbol and Jalali date, safe for a file system", () => {
    expect(gaugeImageFileName("فجر")).toMatch(
      /^گیج-ارزش‌گذاری-فجر-[۰-۹]{4}-[۰-۹]{2}-[۰-۹]{2}\.png$/u,
    );
    expect(gaugeImageFileName('و/"بملت')).toMatch(/^گیج-ارزش‌گذاری-وبملت-/u);
  });
});

describe("comparisonImageFileName", () => {
  it("names the picture by the Jalali date", () => {
    expect(comparisonImageFileName()).toMatch(/^مقایسه-با-صنعت-[۰-۹]{4}-[۰-۹]{2}-[۰-۹]{2}\.png$/u);
  });
});
