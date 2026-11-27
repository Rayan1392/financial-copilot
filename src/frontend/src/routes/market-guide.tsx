import { createFileRoute, Link, useRouter } from "@tanstack/react-router";
import { ArrowDown, ArrowRight, ChevronLeft, Copy, Maximize2 } from "lucide-react";
import { Content as DialogSurface } from "@radix-ui/react-dialog";
import { useRef, useState } from "react";
import { SapioMark } from "@/components/app/sapio-mark";
import { ThemeToggle } from "@/components/app/theme-toggle";
import {
  Dialog,
  DialogClose,
  DialogOverlay,
  DialogPortal,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { isAuthenticated } from "@/integrations/financial-copilot/auth";
import { clientMobileRedirectTarget, readExperiencePreference } from "@/lib/experience/client";
import {
  MARKET_GUIDE_TITLE,
  marketGuideGroups,
  type GuideExample,
} from "@/lib/market-guide/catalog";
import {
  guideDestination,
  guideReturnPath,
  parseGuideSearch,
  type GuideSource,
} from "@/lib/market-guide/navigation";
import { cn } from "@/lib/utils";

export const Route = createFileRoute("/market-guide")({
  validateSearch: parseGuideSearch,
  head: () => ({ meta: [{ title: `${MARKET_GUIDE_TITLE} — ساپیو` }] }),
  component: MarketGuideRoute,
});

const control =
  "inline-flex min-h-11 items-center justify-center gap-2 rounded-xl px-3 py-2 text-sm font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring";
const secondary = `${control} border border-border bg-card text-foreground hover:bg-accent`;
const primary = `${control} bg-primary text-primary-foreground hover:brightness-110`;
const workflowLabels = {
  watchlist: "باز کردن دیده‌بان",
  events: "باز کردن رویدادهای دیده‌بان",
  telegram: "باز کردن اتصال تلگرام",
  support: "باز کردن پیام‌ها",
};

function MarketGuideRoute() {
  const { example, source } = Route.useSearch();
  return <MarketGuide key={example ?? "default"} initialExample={example} source={source} />;
}

function MarketGuide({
  initialExample,
  source,
}: {
  initialExample?: string;
  source?: GuideSource;
}) {
  const router = useRouter();
  const [group, setGroup] = useState(
    () =>
      marketGuideGroups.find((item) =>
        item.examples.some((example) => example.id === initialExample),
      ) ?? marketGuideGroups[0],
  );
  const [example, setExample] = useState(
    () => group.examples.find((item) => item.id === initialExample) ?? group.examples[0],
  );
  const [copyStatus, setCopyStatus] = useState("");
  const [opening, setOpening] = useState(false);
  const [actionError, setActionError] = useState("");
  const promptRef = useRef<HTMLParagraphElement>(null);
  const monthlyImage = marketGuideGroups[0].examples[0].image!;

  function selectExample(item: GuideExample) {
    setExample(item);
    setCopyStatus("");
    setActionError("");
  }

  async function copyExample() {
    try {
      await navigator.clipboard.writeText(example.prompt);
      setCopyStatus("پرسش کپی شد");
    } catch {
      const selection = window.getSelection();
      if (!selection || !promptRef.current) return;
      const range = document.createRange();
      range.selectNodeContents(promptRef.current);
      selection.removeAllRanges();
      selection.addRange(range);
      setCopyStatus("پرسش برای کپی انتخاب شد");
    }
  }

  async function openExample() {
    setOpening(true);
    setActionError("");
    const experience =
      clientMobileRedirectTarget("/chat") === "/m"
        ? "mobile"
        : (source ?? readExperiencePreference() ?? "desktop");
    try {
      if (example.action === "chat") {
        if (!(await isAuthenticated())) {
          await router.navigate({
            to: "/auth",
            search: { redirect: guideReturnPath(example.id, experience) },
          });
          return;
        }
        await router.navigate({
          ...guideDestination("chat", experience),
          state: { prefillQuestion: example.prompt },
        });
        return;
      }
      await router.navigate(guideDestination(example.action, experience));
    } catch {
      setActionError("باز کردن این بخش ممکن نشد. دوباره تلاش کنید.");
    } finally {
      setOpening(false);
    }
  }

  return (
    <main dir="rtl" className="min-h-screen bg-background text-foreground">
      <div className="mx-auto max-w-7xl px-4 pb-8 sm:px-8">
        <div className="flex min-h-17 items-center justify-between gap-3">
          <Link
            to={source === "mobile" ? "/m/account" : source === "desktop" ? "/chat" : "/"}
            className="flex min-h-11 items-center gap-2 rounded-xl text-sm font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          >
            <ArrowRight className="size-4" aria-hidden="true" />
            <SapioMark className="size-8 text-emerald" />
            بازگشت به ساپیو
          </Link>
          <ThemeToggle />
        </div>

        <header className="grid items-center gap-6 rounded-3xl border border-emerald/20 bg-linear-to-bl from-emerald-soft via-surface to-surface p-5 sm:p-8 md:grid-cols-2 lg:gap-12 lg:p-12">
          <div>
            <h1 className="text-3xl leading-tight font-bold text-balance sm:text-4xl lg:text-5xl">
              {MARKET_GUIDE_TITLE}
            </h1>
            <p className="mt-4 max-w-prose text-base leading-8 text-muted-foreground">
              نمونهٔ پاسخ‌های ساپیو را ببینید و پرسش مناسب برای تحلیل سهم، مقایسهٔ صنعت یا
              دنبال‌کردن رویدادهای بازار را پیدا کنید.
            </p>
            <a href="#guide-examples" className={`${primary} mt-5`}>
              دیدن نمونه پاسخ‌ها <ArrowDown className="size-4" aria-hidden="true" />
            </a>
          </div>
          <figure className="overflow-hidden rounded-2xl border border-border bg-card shadow-sm">
            <img
              src="/market-guide/monthly-preview.webp"
              alt={monthlyImage.alt}
              width={monthlyImage.previewWidth}
              height={monthlyImage.previewHeight}
              className="h-auto w-full"
              fetchPriority="high"
            />
            <figcaption className="px-3 py-2 text-sm text-muted-foreground">
              نمونهٔ پاسخ ساپیو · روند فروش ماهانهٔ زامیاد
            </figcaption>
          </figure>
        </header>

        <section
          id="guide-examples"
          className="mt-9 scroll-mt-4 sm:mt-12"
          aria-labelledby="guide-examples-title"
        >
          <h2 id="guide-examples-title" className="text-2xl font-bold">
            از ساپیو چه بپرسیم؟
          </h2>
          <p className="mt-1 text-sm text-muted-foreground">
            یک موضوع را انتخاب کنید و نمونهٔ پاسخ را ببینید.
          </p>
          <div role="group" aria-label="موضوع‌ها" className="mt-4 flex flex-wrap gap-2">
            {marketGuideGroups.map((item) => (
              <button
                key={item.id}
                type="button"
                aria-pressed={item.id === group.id}
                onClick={() => {
                  if (group.id === item.id) return;
                  setGroup(item);
                  selectExample(item.examples[0]);
                }}
                className={cn(
                  secondary,
                  "rounded-full",
                  item.id === group.id && "border-emerald/40 bg-emerald-soft emerald-ink-text",
                )}
              >
                {item.title}
              </button>
            ))}
          </div>

          <div className="mt-4 grid items-start gap-4 md:grid-cols-[14rem_minmax(0,1fr)] lg:grid-cols-[17rem_minmax(0,1fr)]">
            <div
              role="group"
              aria-label="قابلیت‌های موضوع انتخاب‌شده"
              className="flex flex-wrap gap-1.5 rounded-2xl border border-border bg-surface p-2 md:flex-col"
            >
              {group.examples.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  aria-pressed={item.id === example.id}
                  onClick={() => selectExample(item)}
                  className={cn(
                    control,
                    "flex-1 justify-start text-start hover:bg-accent md:justify-between",
                    item.id === example.id && "bg-emerald-soft emerald-ink-text",
                  )}
                >
                  {item.title}
                  <ChevronLeft className="hidden size-4 shrink-0 md:block" aria-hidden="true" />
                </button>
              ))}
            </div>

            <article
              aria-labelledby="guide-feature-title"
              className="min-w-0 rounded-2xl border border-border bg-card p-4 shadow-sm sm:p-6"
            >
              <h3
                id="guide-feature-title"
                aria-live="polite"
                className="text-xl font-bold sm:text-2xl"
              >
                {example.title}
              </h3>
              <p className="mt-2 leading-7 text-muted-foreground">{example.description}</p>
              <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-xl bg-emerald-soft p-3">
                <div className="min-w-0 flex-[1_1_17rem]">
                  <p className="text-sm font-semibold emerald-ink-text">پرسش نمونه</p>
                  <p ref={promptRef} className="mt-1 font-medium">
                    {example.prompt}
                  </p>
                </div>
                <div className="flex flex-wrap gap-2">
                  <button type="button" onClick={copyExample} className={secondary}>
                    <Copy className="size-4" aria-hidden="true" /> کپی پرسش
                  </button>
                  <button
                    type="button"
                    onClick={openExample}
                    disabled={opening}
                    className={`${primary} disabled:opacity-60`}
                  >
                    {opening
                      ? "در حال باز کردن…"
                      : example.action === "chat"
                        ? "باز کردن در گفتگو"
                        : workflowLabels[example.action]}
                  </button>
                </div>
                <span role="status" className={copyStatus ? "basis-full text-sm" : "sr-only"}>
                  {copyStatus}
                </span>
                {actionError && (
                  <p role="alert" className="basis-full text-sm text-destructive">
                    {actionError}
                  </p>
                )}
              </div>
              {example.image && (
                <GuideCapture key={example.id} example={example} image={example.image} />
              )}
              {example.action === "telegram" && (
                <p className="mt-4 rounded-xl border border-border p-3 text-sm leading-7 text-muted-foreground">
                  در «اتصال تلگرام» حساب را به ربات ساپیو متصل کنید. سپس همین پرسش را در گفت‌وگوی
                  ربات بفرستید.
                </p>
              )}
            </article>
          </div>
        </section>
      </div>
    </main>
  );
}

function GuideCapture({
  example,
  image,
}: {
  example: GuideExample;
  image: NonNullable<GuideExample["image"]>;
}) {
  const [original, setOriginal] = useState(false);
  const viewerRef = useRef<HTMLDivElement>(null);

  function toggleSize() {
    setOriginal((current) => !current);
    viewerRef.current?.scrollTo(0, 0);
  }

  return (
    <Dialog
      onOpenChange={(open) => {
        if (!open) setOriginal(false);
      }}
    >
      <figure className="mt-4">
        <figcaption className="mb-2 flex flex-wrap items-center justify-between gap-2 text-sm text-muted-foreground">
          <span>{image.kind === "screen" ? "تصویر محیط ساپیو" : "تصویر پاسخ ساپیو"}</span>
          <DialogTrigger className={secondary}>
            <Maximize2 className="size-4" aria-hidden="true" /> بزرگ‌نمایی
          </DialogTrigger>
        </figcaption>
        <div className="max-h-144 overflow-hidden rounded-xl border border-border bg-surface">
          <img
            src={`/market-guide/${example.id}-preview.webp`}
            width={image.previewWidth}
            height={image.previewHeight}
            alt={image.alt}
            loading="lazy"
            decoding="async"
            className="mx-auto block h-auto max-w-full"
          />
        </div>
      </figure>
      <DialogPortal>
        <DialogOverlay />
        <DialogSurface
          dir="rtl"
          aria-describedby={undefined}
          className="fixed top-1/2 left-1/2 z-50 flex max-h-[92dvh] w-[calc(100%_-_1rem)] max-w-6xl -translate-x-1/2 -translate-y-1/2 flex-col overflow-hidden rounded-2xl border border-border bg-card text-foreground shadow-xl"
        >
          <div className="flex flex-wrap items-center justify-between gap-2 p-3 sm:px-4">
            <DialogTitle className="font-semibold">{example.title}</DialogTitle>
            <div className="flex flex-wrap gap-2">
              <button
                type="button"
                aria-pressed={original}
                onClick={toggleSize}
                className={secondary}
              >
                {original ? "متناسب با پنجره" : "اندازهٔ اصلی"}
              </button>
              <DialogClose className={secondary}>بستن</DialogClose>
            </div>
          </div>
          <div
            ref={viewerRef}
            tabIndex={0}
            aria-label="تصویر قابل پیمایش"
            className="overflow-auto overscroll-contain bg-surface p-2 focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring"
          >
            <img
              src={`/market-guide/${example.id}-full.png`}
              width={image.width}
              height={image.height}
              alt={image.alt}
              className={cn("block h-auto", original ? "max-w-none" : "mx-auto max-w-full")}
            />
          </div>
        </DialogSurface>
      </DialogPortal>
    </Dialog>
  );
}
