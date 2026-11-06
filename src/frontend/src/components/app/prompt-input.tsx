import { useEffect, useRef, useState } from "react";
import { ArrowUp } from "lucide-react";
import { AssistedQueryDialog } from "@/components/app/assisted-query-dialog";
import { ReleaseVersionLink } from "@/components/release-notes/release-version-link";

interface Props {
  onSubmit: (text: string) => void;
  loading?: boolean;
  showAssistedQuery?: boolean;
  assistedQueryDisabled?: boolean;
  /** Starting text, focused with the caret at its end. Remount (change `key`) to apply a new one. */
  initialValue?: string;
  /** Adds the «نسخهٔ x.y.z · تاریخچهٔ نسخه‌ها» link to the fine print. Home page only; needs a router. */
  showReleaseVersion?: boolean;
}

export function PromptInput({
  onSubmit,
  loading,
  showAssistedQuery = true,
  assistedQueryDisabled = true,
  initialValue = "",
  showReleaseVersion = false,
}: Props) {
  const [value, setValue] = useState(initialValue);
  const ref = useRef<HTMLTextAreaElement>(null);

  // A seeded composer is an invitation to keep typing: focus it with the caret after the seed.
  useEffect(() => {
    const field = ref.current;
    if (!initialValue || !field) return;
    field.focus();
    field.setSelectionRange(initialValue.length, initialValue.length);
  }, [initialValue]);

  function submit() {
    const text = value.trim();
    if (!text || loading) return;
    onSubmit(text);
    setValue("");
    ref.current?.focus();
  }

  return (
    <div className="p-6 flex-shrink-0">
      <div className="relative max-w-3xl mx-auto">
        <div className="rounded-2xl bg-surface ring-1 ring-hairline focus-within:ring-emerald/40 transition-shadow">
          <textarea
            ref={ref}
            value={value}
            onChange={(event) => setValue(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter" && !event.shiftKey) {
                event.preventDefault();
                submit();
              }
            }}
            placeholder="مثلاً: روند فروش ماهانه شاراک را نشان بده"
            rows={2}
            className="w-full bg-transparent border-none resize-none p-4 text-sm focus:outline-none placeholder:text-muted-foreground/60"
          />
          <div className="flex items-center justify-end px-3 pb-3">
            {/* A permanently disabled control invites an action that cannot happen, so the assisted
                query stays hidden until a caller enables it (assistedQueryDisabled={false}). */}
            {showAssistedQuery && !assistedQueryDisabled && (
              <div className="ml-auto">
                <AssistedQueryDialog onCompose={setValue} />
              </div>
            )}
            <button
              onClick={submit}
              disabled={!value.trim() || loading}
              className="size-9 rounded-xl bg-emerald text-primary-foreground flex items-center justify-center hover:brightness-110 transition disabled:opacity-40 disabled:cursor-not-allowed"
              aria-label="ارسال"
            >
              <ArrowUp className="size-4" />
            </button>
          </div>
        </div>
        {/* Fine print. Legal notice: full muted color (no opacity) and 12px keep it at WCAG AA contrast in
            both themes. The release link shares the line on wide screens and wraps under it on narrow ones. */}
        <div className="mt-3 flex flex-wrap items-center justify-center gap-x-4 text-center text-xs leading-5 text-muted-foreground">
          <p>داده‌ها و تحلیل‌ها صرفاً جنبه آموزشی دارند و توصیه سرمایه‌گذاری نیستند.</p>
          {/* No separator glyph: the column narrows with the side panels, and a wrapped line would leave it
              dangling. The gap separates the two on one line; wrapped, the link centers on its own. */}
          {showReleaseVersion && <ReleaseVersionLink className="py-0.5" />}
        </div>
      </div>
    </div>
  );
}
