import { useEffect, useState } from "react";

/** Gaps below this are browser chrome (URL bar, rounding), not a keyboard. */
const MIN_KEYBOARD_PX = 80;
const NON_TEXT_INPUTS = new Set([
  "button",
  "checkbox",
  "color",
  "file",
  "hidden",
  "image",
  "radio",
  "range",
  "reset",
  "submit",
]);

/** The on-screen keyboard can only be open while a text field has focus. */
function textFieldFocused(): boolean {
  const el = document.activeElement;
  if (!(el instanceof HTMLElement)) return false;
  if (el instanceof HTMLTextAreaElement) return true;
  if (el instanceof HTMLInputElement) return !NON_TEXT_INPUTS.has(el.type);
  return el.isContentEditable;
}

/**
 * Keyboard height from viewport metrics, in layout CSS pixels; 0 when no keyboard can be open.
 *
 * Pinch zoom (and zoomed DevTools device emulation) shrinks `visualViewport.height` with no keyboard at all, so the
 * covered part is measured on screen (height × scale), and only while a text field is focused. Treating zoom as a
 * keyboard hid the tab bar and collapsed the screen to its app bar (hotfix 1.12.2). `layoutHeight` is the visible
 * layout height (`documentElement.clientHeight`).
 */
export function measureKeyboardInset(
  viewport: Pick<VisualViewport, "height" | "offsetTop" | "scale">,
  layoutHeight: number,
  textFocused: boolean,
): number {
  if (!textFocused) return 0;
  const scale = viewport.scale || 1;
  const covered = layoutHeight - viewport.height * scale;
  if (covered <= MIN_KEYBOARD_PX) return 0;
  // Unzoomed: lift docked content to the bottom of the visible area (iOS scrolls it by offsetTop).
  const inset =
    Math.abs(scale - 1) < 0.01 ? layoutHeight - viewport.height - viewport.offsetTop : covered;
  return Math.max(0, Math.round(inset));
}

/**
 * Height of the on-screen keyboard, from `visualViewport` (needed on iOS, where the layout viewport does not
 * shrink). Exposed as the `--keyboard-inset` CSS variable on the given element so docked controls sit above it.
 */
export function useKeyboardInset(target: React.RefObject<HTMLElement | null>): number {
  const [inset, setInset] = useState(0);
  useEffect(() => {
    const viewport = window.visualViewport;
    if (!viewport) return;
    const update = () => {
      // documentElement.clientHeight is the visible layout height. innerHeight is not used: Android Chrome grows it
      // far past the screen when the document is wider than the viewport.
      const layoutHeight = document.documentElement.clientHeight || window.innerHeight;
      const value = measureKeyboardInset(viewport, layoutHeight, textFieldFocused());
      setInset(value);
      target.current?.style.setProperty("--keyboard-inset", `${value}px`);
    };
    // Focus moves before the keyboard animates; re-measure after the browser settles activeElement.
    const updateSoon = () => window.setTimeout(update, 0);
    update();
    viewport.addEventListener("resize", update);
    viewport.addEventListener("scroll", update);
    document.addEventListener("focusin", updateSoon);
    document.addEventListener("focusout", updateSoon);
    return () => {
      viewport.removeEventListener("resize", update);
      viewport.removeEventListener("scroll", update);
      document.removeEventListener("focusin", updateSoon);
      document.removeEventListener("focusout", updateSoon);
    };
  }, [target]);
  return inset;
}
