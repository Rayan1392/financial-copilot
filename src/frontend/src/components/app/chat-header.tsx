import { ThemeToggle } from "@/components/app/theme-toggle";

export function ChatHeader() {
  return (
    <header className="h-14 border-b border-hairline flex items-center justify-between px-6 flex-shrink-0">
      <div className="flex items-center gap-4">
        <span className="text-sm font-medium text-muted-foreground">تحلیل لحظه‌ای بازار</span>
        <div className="flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-surface ring-1 ring-hairline">
          <div className="size-1.5 rounded-full bg-emerald animate-pulse" />
          <span className="text-[10px] text-emerald">متصل به نوآوران‌امین</span>
          <div className="size-1.5 rounded-full bg-emerald animate-pulse" />
          <span className="text-[10px] text-emerald">متصل به تحلیل‌اپ</span>

        </div>
      </div>
      <ThemeToggle />
    </header>
  );
}
