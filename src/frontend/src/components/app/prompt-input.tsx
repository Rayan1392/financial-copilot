import { useRef, useState } from "react";
import { ArrowUp, Sparkles, Filter } from "lucide-react";

interface Props {
  onSubmit: (text: string) => void;
  deepResearch: boolean;
  onToggleDeep: () => void;
  loading?: boolean;
}

export function PromptInput({ onSubmit, deepResearch, onToggleDeep, loading }: Props) {
  const [value, setValue] = useState("");
  const ref = useRef<HTMLTextAreaElement>(null);

  function submit() {
    const t = value.trim();
    if (!t || loading) return;
    onSubmit(t);
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
            onChange={(e) => setValue(e.target.value)}
            onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); submit(); } }}
            placeholder="از من درباره نمادها، شاخص یا فیلترهای بازار بپرس..."
            rows={2}
            className="w-full bg-transparent border-none resize-none p-4 text-sm focus:outline-none placeholder:text-muted-foreground/60"
          />
          <div className="flex items-center justify-between px-3 pb-3">
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={onToggleDeep}
                className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-[11px] font-medium transition ring-1 ${
                  deepResearch
                    ? "bg-emerald-soft text-emerald ring-emerald/30"
                    : "bg-background text-muted-foreground ring-hairline hover:text-foreground"
                }`}
              >
                <Sparkles className="size-3" />
                جستجوی عمیق
              </button>
              <button
                type="button"
                className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-[11px] font-medium ring-1 ring-hairline bg-background text-muted-foreground hover:text-foreground transition"
              >
                <Filter className="size-3" />
                فیلترنویسی
              </button>
            </div>
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
