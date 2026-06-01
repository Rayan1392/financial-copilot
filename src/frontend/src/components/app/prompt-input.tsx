import { useRef, useState } from "react";
import { ArrowUp } from "lucide-react";

interface Props {
  onSubmit: (text: string) => void;
  loading?: boolean;
}

export function PromptInput({ onSubmit, loading }: Props) {
  const [value, setValue] = useState("");
  const ref = useRef<HTMLTextAreaElement>(null);

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
            placeholder="از من درباره نمادها، شاخص یا فیلترهای بازار بپرس..."
            rows={2}
            className="w-full bg-transparent border-none resize-none p-4 text-sm focus:outline-none placeholder:text-muted-foreground/60"
          />
          <div className="flex items-center justify-end px-3 pb-3">
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
        <p className="text-[10px] text-muted-foreground/70 text-center mt-3">
          داده‌ها و تحلیل‌ها صرفاً جنبه آموزشی دارند و توصیه سرمایه‌گذاری نیستند.
        </p>
      </div>
    </div>
  );
}
