import { exportDate, safeFileSymbol } from "@/components/app/png-brand";

/** A slow or stuck draw must end in the visible failure state, not in a button that waits forever. */
const RENDER_TIMEOUT_MS = 15_000;
/** Twice the CSS size, so the picture stays sharp when a chat app scales it. */
const PIXEL_RATIO = 2;

/**
 * Draws a mounted element as a PNG. This is the only module that knows the capture library, which is loaded on demand
 * so it stays out of the initial bundle. The node may still be mounting: the time limit covers that wait too.
 */
export async function renderNodeToPngBlob(node: HTMLElement | Promise<HTMLElement>): Promise<Blob> {
  let timer: number | undefined;
  const timeout = new Promise<never>((_, reject) => {
    timer = window.setTimeout(
      () => reject(new Error("The answer image was not ready in time.")),
      RENDER_TIMEOUT_MS,
    );
  });
  try {
    const blob = await Promise.race([draw(node), timeout]);
    if (!blob || blob.size === 0 || blob.type !== "image/png") {
      throw new Error("The answer image is empty.");
    }
    return blob;
  } finally {
    window.clearTimeout(timer);
  }
}

async function draw(node: HTMLElement | Promise<HTMLElement>): Promise<Blob> {
  // Text drawn before the web font is ready would be captured in the fallback face.
  const [{ domToBlob }, element] = await Promise.all([
    import("modern-screenshot"),
    node,
    document.fonts?.ready,
  ]);
  return domToBlob(element, { scale: PIXEL_RATIO, type: "image/png" });
}

/** Whether this browser can put an image on the clipboard. */
export function canCopyImage(): boolean {
  return (
    typeof ClipboardItem !== "undefined" &&
    typeof navigator !== "undefined" &&
    typeof navigator.clipboard?.write === "function"
  );
}

/** Whether this browser's share sheet accepts the file. */
export function canShareFile(file: File): boolean {
  if (typeof navigator === "undefined" || typeof navigator.share !== "function") return false;
  if (typeof navigator.canShare !== "function") return false;
  try {
    return navigator.canShare({ files: [file] });
  } catch {
    return false;
  }
}

/** Whether the share sheet takes PNG files at all, asked with an empty file so nothing is drawn to find out. */
export function canSharePngFiles(): boolean {
  return canShareFile(new File([], "probe.png", { type: "image/png" }));
}

/** File name of a shared gauge picture: the symbol and today's Jalali date. */
export function gaugeImageFileName(symbol: string): string {
  return `گیج-ارزش‌گذاری-${safeFileSymbol(symbol)}-${exportDate().replaceAll("/", "-")}.png`;
}
