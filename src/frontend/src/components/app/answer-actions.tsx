import { useEffect, useRef, useState } from "react";
import { Check, Copy, Image as ImageIcon, RefreshCw, Share2 } from "lucide-react";
import { canCopyImage, canShareFile, canSharePngFiles } from "@/lib/chat/answer-image";
import { answerPlainText, answerTextWithSource } from "@/lib/chat/answer-text";
import { useAnswerImage, type AnswerImageSource } from "@/lib/chat/use-answer-image";

const CONFIRMATION_MS = 2000;
const SHARE_TITLE = "پاسخ ساپیو";

type CopyState = "idle" | "copied" | "failed";
type ImageCopyState = CopyState | "working";

const IMAGE_COPY_LABEL: Record<ImageCopyState, string> = {
  idle: "کپی تصویر",
  working: "در حال ساخت…",
  copied: "تصویر کپی شد",
  failed: "تصویر کپی نشد",
};

/**
 * Actions under an assistant answer: copy its text, share it through the device share sheet (only where the browser
 * supports one), and ask the same question again. Copied and shared text ends with Sapio's name and link. Asking again is a new query and is charged like any other.
 *
 * An answer that offers a picture (`image`) can also be copied as an image, and its share sends the picture with the
 * text as caption. Where the browser cannot take images, the text actions are all there is.
 */
export function AnswerActions({
  text,
  onRegenerate,
  disabled = false,
  image,
}: {
  /** The answer's markdown; copied and shared as plain text. */
  text: string;
  /** Present only for the latest answer that has a question to repeat. */
  onRegenerate?: () => void;
  disabled?: boolean;
  /** Present only for answers whose visual content can leave the app as a picture. */
  image?: AnswerImageSource;
}) {
  const [copyState, setCopyState] = useState<CopyState>("idle");
  const [imageCopyState, setImageCopyState] = useState<ImageCopyState>("idle");
  // True after the browser refused a share because the picture took too long: the next tap sends the kept picture.
  const [imageReadyToShare, setImageReadyToShare] = useState(false);
  const resetTimer = useRef<number | undefined>(undefined);
  const imageResetTimer = useRef<number | undefined>(undefined);
  const { frame, getBlob, warmUp } = useAnswerImage(image);
  const canShare = typeof navigator !== "undefined" && typeof navigator.share === "function";
  const plainText = answerPlainText(text);
  const copiesImage = Boolean(image) && canCopyImage();
  const sharesImage = Boolean(image) && canShare && canSharePngFiles();

  useEffect(
    () => () => {
      window.clearTimeout(resetTimer.current);
      window.clearTimeout(imageResetTimer.current);
    },
    [],
  );

  if (!plainText && !onRegenerate) return null;

  function showCopyState(state: CopyState) {
    setCopyState(state);
    window.clearTimeout(resetTimer.current);
    resetTimer.current = window.setTimeout(() => setCopyState("idle"), CONFIRMATION_MS);
  }

  function showImageCopyState(state: CopyState) {
    setImageCopyState(state);
    window.clearTimeout(imageResetTimer.current);
    imageResetTimer.current = window.setTimeout(() => setImageCopyState("idle"), CONFIRMATION_MS);
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(answerTextWithSource(text));
      showCopyState("copied");
    } catch {
      showCopyState("failed");
    }
  }

  function copyImage() {
    window.clearTimeout(imageResetTimer.current);
    setImageCopyState("working");
    // The clipboard call stays inside the tap and takes the picture as a promise: Safari refuses a write that starts
    // after an await.
    const blob = getBlob();
    blob.catch(() => {});
    try {
      navigator.clipboard.write([new ClipboardItem({ "image/png": blob })]).then(
        () => showImageCopyState("copied"),
        () => showImageCopyState("failed"),
      );
    } catch {
      showImageCopyState("failed");
    }
  }

  async function shareText() {
    try {
      await navigator.share({ title: SHARE_TITLE, text: answerTextWithSource(text) });
    } catch {
      // The person closed the share sheet, or the browser refused; nothing to report.
    }
  }

  async function share() {
    if (!image || !sharesImage) return shareText();
    let file: File;
    try {
      file = new File([await getBlob()], image.fileName, { type: "image/png" });
    } catch {
      return shareText();
    }
    if (!canShareFile(file)) return shareText();
    try {
      await navigator.share({
        files: [file],
        title: SHARE_TITLE,
        text: answerTextWithSource(text),
      });
      setImageReadyToShare(false);
    } catch (error) {
      // NotAllowedError: the tap is too old for the browser. The picture is kept, so one more tap sends it.
      // Anything else is the person closing the sheet.
      setImageReadyToShare(error instanceof Error && error.name === "NotAllowedError");
    }
  }

  const buttonClass =
    "inline-flex min-h-8 items-center gap-1 rounded-md px-2 text-[11px] text-muted-foreground transition-colors hover:bg-white/5 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40 disabled:cursor-not-allowed disabled:opacity-50";
  // Drawing starts when the pointer or focus reaches an image action, so the picture is usually ready at the tap.
  const warmUpHandlers = { onPointerEnter: warmUp, onFocus: warmUp, onTouchStart: warmUp };

  return (
    <div role="group" aria-label="کارهای پاسخ" className="-ms-2 flex flex-wrap items-center gap-1">
      {plainText && (
        <button type="button" onClick={copy} className={buttonClass}>
          {copyState === "copied" ? (
            <Check className="size-3.5 text-emerald" aria-hidden="true" />
          ) : (
            <Copy className="size-3.5" aria-hidden="true" />
          )}
          <span aria-live="polite">
            {copyState === "copied" ? "کپی شد" : copyState === "failed" ? "کپی نشد" : "کپی"}
          </span>
        </button>
      )}
      {plainText && copiesImage && (
        <button
          type="button"
          onClick={copyImage}
          disabled={imageCopyState === "working"}
          className={buttonClass}
          {...warmUpHandlers}
        >
          {imageCopyState === "copied" ? (
            <Check className="size-3.5 text-emerald" aria-hidden="true" />
          ) : (
            <ImageIcon className="size-3.5" aria-hidden="true" />
          )}
          <span aria-live="polite">{IMAGE_COPY_LABEL[imageCopyState]}</span>
        </button>
      )}
      {plainText && canShare && (
        <button
          type="button"
          onClick={share}
          className={buttonClass}
          {...(sharesImage ? warmUpHandlers : {})}
        >
          <Share2 className="size-3.5" aria-hidden="true" />
          {imageReadyToShare ? "ارسال تصویر" : "اشتراک‌گذاری"}
        </button>
      )}
      {onRegenerate && (
        <button
          type="button"
          onClick={() => !disabled && onRegenerate()}
          disabled={disabled}
          className={buttonClass}
          title="همین پرسش دوباره پرسیده می‌شود و اعتبار مصرف می‌کند"
        >
          <RefreshCw className="size-3.5" aria-hidden="true" />
          پاسخ دوباره
        </button>
      )}
      {frame}
    </div>
  );
}
