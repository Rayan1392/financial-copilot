import { Receipt } from "lucide-react";
import type { MyTopUpPurchase } from "@/lib/market-view.functions";
import { formatNumber } from "@/lib/format/persian";
import { useMyTopUps } from "@/lib/workspace/queries";

const STATUS: Record<string, { label: string; tone: string }> = {
  granted: { label: "به حساب اضافه شد", tone: "bg-emerald-soft/70 text-emerald" },
  "expired-unfulfilled": { label: "اعمال نشد · پایان اشتراک", tone: "bg-rose/10 text-rose" },
  "ineligible-unfulfilled": { label: "در انتظار بررسی پشتیبانی", tone: "bg-gold/15 text-gold" },
  "ambiguous-unfulfilled": { label: "ارجاع به پشتیبانی", tone: "bg-gold/15 text-gold" },
};
const UNKNOWN_STATUS = { label: "در حال پردازش", tone: "bg-muted text-muted-foreground" };

function purchaseDate(iso: string) {
  return new Date(iso).toLocaleDateString("fa-IR", {
    year: "numeric",
    month: "long",
    day: "numeric",
  });
}

/** The user's own TahlilApp credit purchases, newest first, for the account page. */
export function PurchaseHistory({ limit = 10 }: { limit?: number }) {
  const topUps = useMyTopUps(limit);
  const items: readonly MyTopUpPurchase[] = topUps.data?.items ?? [];

  return (
    <section
      aria-labelledby="purchase-history-heading"
      className="rounded-2xl bg-surface p-4 ring-1 ring-hairline"
    >
      <h2
        id="purchase-history-heading"
        className="flex items-center gap-2 text-[15px] font-semibold"
      >
        <Receipt className="size-4 text-muted-foreground" aria-hidden="true" />
        تاریخچهٔ خرید اعتبار
      </h2>
      {topUps.isLoading ? (
        <p className="mt-3 text-sm text-muted-foreground">در حال بارگذاری…</p>
      ) : topUps.isError ? (
        <div role="alert" className="mt-3 text-sm">
          <p className="text-muted-foreground">تاریخچهٔ خرید در دسترس نیست.</p>
          <button
            type="button"
            onClick={() => void topUps.refetch()}
            className="mt-2 min-h-10 rounded-xl bg-surface-2 px-4 text-sm font-medium focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          >
            تلاش مجدد
          </button>
        </div>
      ) : items.length === 0 ? (
        <p className="mt-3 text-sm leading-7 text-muted-foreground">
          هنوز خرید اعتباری برای این حساب ثبت نشده است.
        </p>
      ) : (
        <ul className="mt-3 divide-y divide-hairline">
          {items.map((purchase) => {
            const status = STATUS[purchase.status] ?? UNKNOWN_STATUS;
            return (
              <li
                key={purchase.invoiceId}
                className="flex items-center justify-between gap-3 py-2.5"
              >
                <div className="min-w-0">
                  <p className="text-sm font-medium text-foreground tabular-nums">
                    {formatNumber(purchase.creditsPurchased)} اعتبار
                  </p>
                  <p className="mt-0.5 text-xs text-muted-foreground">
                    {purchaseDate(purchase.paidAtUtc)}
                    {purchase.status === "granted" && purchase.expiresAtUtc
                      ? ` · معتبر تا ${purchaseDate(purchase.expiresAtUtc)}`
                      : ""}
                  </p>
                </div>
                <span
                  className={`shrink-0 rounded-full px-2.5 py-1 text-[11px] font-medium ${status.tone}`}
                >
                  {status.label}
                </span>
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
