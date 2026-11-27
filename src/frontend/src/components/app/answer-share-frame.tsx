import type { ReactNode, Ref } from "react";
import { createPortal } from "react-dom";
import { SAPIO_NAME_LINE, SAPIO_SITE_URL } from "@/lib/chat/answer-text";

type PictureProps = {
  /** Width of the picture in CSS pixels. */
  width: number;
  children: ReactNode;
  ref?: Ref<HTMLDivElement>;
};

/**
 * What a shared answer picture shows: the answer's content at a fixed width on the page background, ending with
 * Sapio's name and address. Inside it animations are off and on-screen-only controls are hidden (see `styles.css`).
 */
export function AnswerSharePicture({ width, children, ref }: PictureProps) {
  return (
    <div
      ref={ref}
      data-share-frame
      data-testid="answer-share-frame"
      dir="rtl"
      className="bg-background p-6 text-right text-foreground"
      style={{ width }}
    >
      {children}
      <div className="mt-4 flex items-baseline justify-between gap-4 text-sm text-muted-foreground">
        <span>{SAPIO_NAME_LINE}</span>
        <span dir="ltr">{new URL(SAPIO_SITE_URL).host}</span>
      </div>
    </div>
  );
}

/**
 * The off-screen copy of an answer that is captured as its shared picture. Its fixed width makes the picture the same
 * on a phone and on a desktop. It is mounted only while a picture is drawn. `ref` receives the element to capture:
 * the picture itself, without the off-screen positioning.
 */
export function AnswerShareFrame(props: PictureProps) {
  return createPortal(
    // Fixed boxes add no scrollable overflow; inert and aria-hidden keep the copy away from focus and screen readers.
    <div aria-hidden inert className="pointer-events-none fixed top-0" style={{ left: -100000 }}>
      <AnswerSharePicture {...props} />
    </div>,
    document.body,
  );
}
