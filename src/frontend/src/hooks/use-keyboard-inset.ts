import { useEffect, useState } from "react";

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
      const next = Math.max(
        0,
        Math.round(window.innerHeight - viewport.height - viewport.offsetTop),
      );
      // Small differences come from browser chrome, not a keyboard.
      const value = next > 80 ? next : 0;
      setInset(value);
      target.current?.style.setProperty("--keyboard-inset", `${value}px`);
    };
    update();
    viewport.addEventListener("resize", update);
    viewport.addEventListener("scroll", update);
    return () => {
      viewport.removeEventListener("resize", update);
      viewport.removeEventListener("scroll", update);
    };
  }, [target]);
  return inset;
}
