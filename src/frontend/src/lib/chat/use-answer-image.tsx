import { useCallback, useEffect, useRef, useState, type ReactNode } from "react";
import { AnswerShareFrame } from "@/components/app/answer-share-frame";
import { renderNodeToPngBlob } from "@/lib/chat/answer-image";

/** What an answer offers as its shared picture. */
export type AnswerImageSource = {
  /** The answer's visual content, rendered a second time off-screen at `width`. */
  content: ReactNode;
  /** Width of the picture in CSS pixels. */
  width: number;
  fileName: string;
};

const currentTheme = () =>
  document.documentElement.classList.contains("light") ? "light" : "dark";

const nextFrame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

/**
 * Draws an answer's picture on demand and keeps it for the answer. `getBlob` mounts the off-screen frame, captures it
 * and removes it again, also on failure. Calls made while a draw runs, and later calls in the same theme, get the same
 * picture; a theme change or a failed draw makes the next call draw again. The caller renders `frame`.
 */
export function useAnswerImage(image: AnswerImageSource | undefined) {
  const [mounted, setMounted] = useState(false);
  const cache = useRef<{ theme: string; blob: Promise<Blob> } | null>(null);
  const node = useRef<HTMLDivElement | null>(null);
  const nodeWaiter = useRef<((element: HTMLDivElement) => void) | null>(null);

  useEffect(
    () => () => {
      cache.current = null;
    },
    [],
  );

  const onNode = useCallback((element: HTMLDivElement | null) => {
    node.current = element;
    if (element) nodeWaiter.current?.(element);
  }, []);

  const getBlob = useCallback((): Promise<Blob> => {
    const theme = currentTheme();
    if (cache.current?.theme === theme) return cache.current.blob;

    const laidOut = (async () => {
      const element =
        node.current ??
        (await new Promise<HTMLDivElement>((resolve) => {
          nodeWaiter.current = resolve;
        }));
      // Two frames: the container queries inside the content settle after the first layout.
      await nextFrame();
      await nextFrame();
      return element;
    })();
    setMounted(true);
    const blob = renderNodeToPngBlob(laidOut).finally(() => {
      nodeWaiter.current = null;
      setMounted(false);
    });
    const entry = { theme, blob };
    cache.current = entry;
    blob.catch(() => {
      if (cache.current === entry) cache.current = null;
    });
    return blob;
  }, []);

  /** Starts drawing ahead of the tap, so the picture is ready while the browser still accepts a share. */
  const warmUp = useCallback(() => {
    getBlob().catch(() => {});
  }, [getBlob]);

  const frame =
    mounted && image ? (
      <AnswerShareFrame ref={onNode} width={image.width}>
        {image.content}
      </AnswerShareFrame>
    ) : null;

  return { frame, getBlob, warmUp };
}
